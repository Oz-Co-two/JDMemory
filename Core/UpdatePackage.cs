using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace JDMemory.Core
{
    /// <summary>update.json を読んだ結果、どうするか</summary>
    internal sealed class UpdateDecision
    {
        /// <summary>新しい版があるか（無ければ何も出さない）</summary>
        public bool Newer;

        /// <summary>自動で取得して準備してよいか</summary>
        public bool Download;

        /// <summary>例: v0.2.0</summary>
        public string Label;
        public Version Version;

        /// <summary>自動で準備しない理由（知らせに添える）</summary>
        public string Note;

        public string ZipUrl;
        public string Sha256;
        public long Size;
    }

    /// <summary>
    /// 新しい版の判定・照合・配置（Unity に依存しない部分。ゲームの外からも試せる）。
    /// 通信は Managers.UpdateChecker が行う。照合の決まりはインストーラーの UpdateClient と同じ:
    ///  - update.json の name・format・version、zip の名前（JDMemory-v〇.〇.〇.zip）・SHA-256・サイズ
    ///  - zip から読むのは Plugins/JDMemory.dll だけ。取り出した DLL の名前と版が update.json と同じこと
    /// </summary>
    internal static class UpdatePackage
    {
        internal const string Repo = "Oz-Co-two/JDMemory";
        internal const string RepoUrl = "https://github.com/" + Repo;
        internal const string UpdateUrl = RepoUrl + "/releases/latest/download/update.json";
        internal const int Format = 1;
        internal const int MaxManifest = 64 * 1024;
        internal const int MaxZip = 20 * 1024 * 1024;
        private const int MaxDll = 10 * 1024 * 1024;
        private const string DllEntry = "Plugins/JDMemory.dll";

        /// <summary>update.json を読んで、知らせるか・自動で取るかを決める。読めない・自分の物でない時は null</summary>
        internal static UpdateDecision Decide(string json, Version current, string gameVersion, bool autoUpdate)
        {
            JObject manifest;
            try
            {
                manifest = JObject.Parse(json);
            }
            catch (Exception)
            {
                return null;
            }
            if ((string)manifest["name"] != "JDMemory")
            {
                return null;
            }
            string tag = (string)manifest["version"];
            Version latest = ParseVersion(tag);
            if (latest == null)
            {
                return null;
            }

            var decision = new UpdateDecision { Version = latest, Label = "v" + latest.ToString(3) };
            if (latest <= current)
            {
                return decision;
            }
            decision.Newer = true;

            if (!autoUpdate)
            {
                return decision;
            }
            var gameVersions = manifest["gameVersions"] as JArray;
            if (gameVersion == null || gameVersions == null || !gameVersions.Any(v => (string)v == gameVersion))
            {
                decision.Note = $"ゲーム {gameVersion ?? "不明"} は対象外のため、自動では入れません";
                return decision;
            }

            var zip = manifest["zip"] as JObject;
            string zipName = (string)zip?["name"];
            string sha256 = (string)zip?["sha256"];
            long size = zip?["size"]?.Type == JTokenType.Integer ? (long)zip["size"] : 0;
            bool ok = manifest["format"]?.Type == JTokenType.Integer && (int)manifest["format"] == Format
                      && zipName == "JDMemory-" + tag + ".zip"
                      && sha256 != null && Regex.IsMatch(sha256, "^[0-9a-f]{64}$")
                      && size > 0 && size <= MaxZip;
            if (!ok)
            {
                decision.Note = "自動では取れない形式です";
                return decision;
            }

            decision.Download = true;
            decision.ZipUrl = RepoUrl + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(zipName);
            decision.Sha256 = sha256;
            decision.Size = size;
            return decision;
        }

        /// <summary>zip を照合して、中の JDMemory.dll を取り出す。合わなければ InvalidDataException</summary>
        internal static byte[] ExtractDll(byte[] zip, string sha256, long size)
        {
            if (zip == null || zip.Length != size)
            {
                throw new InvalidDataException("大きさが合いません");
            }
            string hash;
            using (var sha = SHA256.Create())
            {
                hash = string.Concat(sha.ComputeHash(zip).Select(b => b.ToString("x2")));
            }
            if (hash != sha256)
            {
                throw new InvalidDataException("公開された物と一致しません（SHA-256）");
            }
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == DllEntry);
                if (entry == null || entry.Length <= 0 || entry.Length > MaxDll)
                {
                    throw new InvalidDataException("zip の中に JDMemory.dll がありません");
                }
                using (var stream = entry.Open())
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// 取り出した DLL を一時ファイルに書いて名前と版を確かめてから、pendingDll（IPA\Pending\Plugins\JDMemory.dll）に置く。
        /// 一時ファイルは Pending の外（tmpDir）に作る（途中で止まっても、BSIPA がゴミをゲームのフォルダへ写さないように）
        /// </summary>
        internal static void Stage(byte[] dll, Version expected, string tmpDir, string pendingDll)
        {
            Directory.CreateDirectory(tmpDir);
            string tmp = Path.Combine(tmpDir, "update.dll.tmp");
            File.WriteAllBytes(tmp, dll);
            try
            {
                var name = AssemblyName.GetAssemblyName(tmp);
                var v = name.Version;
                if (name.Name != "JDMemory" || v == null || new Version(v.Major, v.Minor, Math.Max(0, v.Build)) != expected)
                {
                    throw new InvalidDataException("取り出した DLL の版が合いません");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(pendingDll));
                if (File.Exists(pendingDll))
                {
                    File.Delete(pendingDll);
                }
                File.Move(tmp, pendingDll);
            }
            finally
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
        }

        /// <summary>準備した物を消し、空になったフォルダだけ片付ける（ほかの MOD の物には触らない）</summary>
        internal static void Cancel(string pendingDll, string pendingDir)
        {
            if (File.Exists(pendingDll))
            {
                File.Delete(pendingDll);
            }
            DeleteIfEmpty(Path.GetDirectoryName(pendingDll));
            DeleteIfEmpty(pendingDir);
        }

        private static void DeleteIfEmpty(string dir)
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }

        /// <summary>「v1.2.3」「1.2.3」を Version にする。読めなければ null</summary>
        internal static Version ParseVersion(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            string s = text.Trim().TrimStart('v', 'V');
            int cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
            {
                s = s.Substring(0, cut);
            }
            var parts = s.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }
            return int.TryParse(parts[0], out int a) && int.TryParse(parts[1], out int b) && int.TryParse(parts[2], out int c)
                ? new Version(a, b, c)
                : null;
        }
    }
}
