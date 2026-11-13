using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace JDMemoryInstaller
{
    /// <summary>
    /// GitHub の最新リリースから新しい版を取る（blnext の core/app_update.py と同じ考え方）。
    ///  - 取得先 URL はここに固定（HTTPS）。画面から URL・パスを受け取らない
    ///  - update.json（tools/make_release.ps1 が作る）を検査し、zip は SHA-256 とサイズを照合してから中を読む
    ///  - zip から読むのは Plugins/JDMemory.dll だけ
    /// ★版をまたぐ約束（変えると古いインストーラーが新しい版を取れなくなる）:
    ///    Repo の場所・update.json の置き場所と形式（Format）・zip の名前（JDMemory-v〇.〇.〇.zip）と中の Plugins/JDMemory.dll
    /// </summary>
    internal static class UpdateClient
    {
        internal const string Repo = "Oz-Co-two/JDMemory";
        internal const string RepoUrl = "https://github.com/" + Repo;
        internal const string ReleasesUrl = RepoUrl + "/releases/latest";
        private const string UpdateUrl = RepoUrl + "/releases/latest/download/update.json";
        private const int Format = 1;
        private const int MaxManifest = 64 * 1024;
        private const long MaxZip = 20L * 1024 * 1024;
        private const long MaxDll = 10L * 1024 * 1024;
        private const string DllEntry = "Plugins/JDMemory.dll";

        [DataContract]
        internal sealed class FileEntry
        {
            [DataMember(Name = "name")] public string Name = null;
            [DataMember(Name = "sha256")] public string Sha256 = null;
            [DataMember(Name = "size")] public long Size = 0;
        }

        [DataContract]
        internal sealed class UpdateManifest
        {
            [DataMember(Name = "name")] public string Name = null;
            [DataMember(Name = "format")] public int FormatVersion = 0;
            [DataMember(Name = "version")] public string Version = null;
            [DataMember(Name = "gameVersions")] public string[] GameVersions = null;
            [DataMember(Name = "zip")] public FileEntry Zip = null;
            [DataMember(Name = "notes")] public string[] Notes = null;
        }

        internal sealed class UpdateException : Exception
        {
            public UpdateException(string message) : base(message) { }
        }

        private static byte[] Get(string url, long limit)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "JDMemoryInstaller/" + typeof(UpdateClient).Assembly.GetName().Version.ToString(3);
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            request.AllowAutoRedirect = true;
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, read);
                        if (ms.Length > limit)
                        {
                            throw new UpdateException("取得したファイルが大きすぎます");
                        }
                    }
                    return ms.ToArray();
                }
            }
            catch (WebException e) when ((e.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                throw new UpdateException("GitHub に公開された版がまだありません");
            }
            catch (WebException e)
            {
                throw new UpdateException("GitHub に接続できませんでした（" + e.Status + "）");
            }
        }

        /// <summary>最新版の update.json を読んで検査する</summary>
        internal static UpdateManifest CheckLatest()
        {
            return ParseManifest(Get(UpdateUrl, MaxManifest));
        }

        /// <summary>update.json を検査する（tools/make_release.ps1 も、作った物をこれに通して確かめる）</summary>
        internal static UpdateManifest ParseManifest(byte[] raw)
        {
            UpdateManifest m;
            try
            {
                using (var stream = new MemoryStream(raw))
                {
                    m = (UpdateManifest)new DataContractJsonSerializer(typeof(UpdateManifest)).ReadObject(stream);
                }
            }
            catch (Exception)
            {
                throw new UpdateException("最新版の情報（update.json）を読めませんでした");
            }
            if (m == null || m.Name != "JDMemory" || ModPackage.ParseVersion(m.Version) == null)
            {
                throw new UpdateException("最新版の情報（update.json）の形が違います");
            }
            return m;
        }

        /// <summary>このインストーラーで自動で取れる形か（形式が新しすぎる時は、リリースのページを案内する）</summary>
        internal static bool CanDownload(UpdateManifest m)
        {
            var z = m.Zip;
            return m.FormatVersion == Format && z != null
                && z.Name == "JDMemory-" + m.Version + ".zip"
                && z.Sha256 != null && Regex.IsMatch(z.Sha256, "^[0-9a-f]{64}$")
                && z.Size > 0 && z.Size <= MaxZip;
        }

        /// <summary>zip を取って照合し、中の JDMemory.dll を返す</summary>
        internal static ModPackage Download(UpdateManifest m)
        {
            if (!CanDownload(m))
            {
                throw new UpdateException("このインストーラーでは自動で取れない形式です。GitHub のページから新しいインストーラーを入手してください");
            }
            string url = RepoUrl + "/releases/download/" + Uri.EscapeDataString(m.Version) + "/" + Uri.EscapeDataString(m.Zip.Name);
            return ExtractPackage(m, Get(url, MaxZip));
        }

        /// <summary>zip を update.json と照合して、中の JDMemory.dll を取り出す（tools/make_release.ps1 も使う）</summary>
        internal static ModPackage ExtractPackage(UpdateManifest m, byte[] zip)
        {
            if (!CanDownload(m))
            {
                throw new UpdateException("このインストーラーでは自動で取れない形式です");
            }
            if (zip.Length != m.Zip.Size)
            {
                throw new UpdateException("取得したファイルの大きさが合いません（壊れているか、途中で切れました）");
            }
            string hash;
            using (var sha = SHA256.Create())
            {
                hash = string.Concat(sha.ComputeHash(zip).Select(b => b.ToString("x2")));
            }
            if (hash != m.Zip.Sha256)
            {
                throw new UpdateException("取得したファイルが公開された物と一致しません（SHA-256 が違います）");
            }

            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == DllEntry);
                if (entry == null || entry.Length <= 0 || entry.Length > MaxDll)
                {
                    throw new UpdateException("取得した zip の中に JDMemory.dll がありません");
                }
                using (var stream = entry.Open())
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return new ModPackage
                    {
                        Version = m.Version.TrimStart('v', 'V'),
                        GameVersions = m.GameVersions ?? new string[0],
                        Dll = ms.ToArray(),
                        Bundled = false,
                    };
                }
            }
        }
    }
}
