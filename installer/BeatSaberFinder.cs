using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace JDMemoryInstaller
{
    /// <summary>見つかった Beat Saber のフォルダ</summary>
    internal sealed class BeatSaberLocation
    {
        public string Path;
        /// <summary>bsmanager / steam / oculus / manual</summary>
        public string Source;
        /// <summary>BSManager で最後に起動した版</summary>
        public bool LastLaunched;
    }

    /// <summary>
    /// Beat Saber のフォルダ（Beat Saber.exe がある所）を探す。
    /// blnext（core/output/bsfind.py）の探し方を C# に移した物:
    ///  - BSManager: %APPDATA%\bs-manager\config.json の installation-folder と、初期値のホームの下の BSManager\BSInstances\&lt;版&gt;。
    ///    最後に起動した版を先頭に
    ///  - Steam: レジストリの InstallPath / SteamPath と steamapps\libraryfolders.vdf の各ライブラリ
    ///  - Meta(Oculus): レジストリの Libraries の OriginalPath
    ///  - 選んだフォルダからは、上へたどって Beat Saber.exe のある所にする（中を選んでも、1つ上を選んでもよい）
    ///  - ネットワーク上のパス（\\server\...）は触らない（存在を確かめるだけで接続と認証が起きるため）
    /// </summary>
    internal static class BeatSaberFinder
    {
        private const string Exe = "Beat Saber.exe";
        private static readonly string[] SteamSub = { "steamapps", "common", "Beat Saber" };
        private static readonly string[] OculusSub = { "Software", "hyperbolic-magnetism-beat-saber" };
        private const string BsmInstances = "BSInstances";
        private const int UpLevels = 6;

        private static bool IsLocal(string p)
        {
            return !string.IsNullOrEmpty(p) && Regex.IsMatch(p, @"^[A-Za-z]:[\\/]");
        }

        internal static bool IsRoot(string p)
        {
            try
            {
                return IsLocal(p) && File.Exists(System.IO.Path.Combine(p, Exe));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Norm(string p)
        {
            return System.IO.Path.GetFullPath(p).TrimEnd('\\', '/').ToLowerInvariant();
        }

        /// <summary>選んだフォルダから Beat Saber のフォルダの候補を返す（重複なし）。見つからなければ空</summary>
        internal static List<string> Resolve(string path)
        {
            if (!IsLocal(path))
            {
                return new List<string>();
            }
            string cur = System.IO.Path.GetFullPath(path);
            for (int i = 0; i <= UpLevels && cur != null; i++)
            {
                if (IsRoot(cur))
                {
                    return new List<string> { cur };
                }
                var inst = Instances(System.IO.Path.Combine(cur, BsmInstances));
                if (inst.Count > 0)
                {
                    return inst;
                }
                cur = System.IO.Path.GetDirectoryName(cur);
            }

            // 1つ上の階層（steamapps\common や BSManager など）を選んだ時は中から探す
            var found = new List<string>();
            string baseDir = System.IO.Path.GetFullPath(path);
            var subs = new List<string[]>
            {
                new string[0],
                new[] { "Beat Saber" },
                SteamSub,
                OculusSub,
                new[] { "BSManager", BsmInstances },
            };
            foreach (var sub in subs)
            {
                string d = System.IO.Path.Combine(new[] { baseDir }.Concat(sub).ToArray());
                if (sub.Length > 0 && IsRoot(d))
                {
                    found.Add(d);
                }
                try
                {
                    if (Directory.Exists(d))
                    {
                        found.AddRange(Directory.GetDirectories(d).Where(IsRoot));
                    }
                }
                catch (Exception)
                {
                    // 読めないフォルダは飛ばす
                }
            }
            return Unique(found);
        }

        private static List<string> Instances(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    return new List<string>();
                }
                return Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Where(IsRoot).ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        private static List<string> Unique(IEnumerable<string> paths)
        {
            var seen = new HashSet<string>();
            var result = new List<string>();
            foreach (var p in paths)
            {
                if (seen.Add(Norm(p)))
                {
                    result.Add(p);
                }
            }
            return result;
        }

        /// <summary>Steam の libraryfolders.vdf からライブラリのパスを取り出す</summary>
        internal static List<string> ParseLibraryFolders(string text)
        {
            return Regex.Matches(text ?? "", "\"path\"\\s+\"([^\"]+)\"")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.Replace("\\\\", "\\"))
                .ToList();
        }

        private static string RegValue(RegistryHive hive, string key, string name)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                using (var k = root.OpenSubKey(key))
                {
                    return k?.GetValue(name) as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<string> SteamLibraries()
        {
            // HKLM の InstallPath を先に（HKCU の SteamPath は小文字・/ 区切りのことがあり、表示が読みにくい）
            var roots = new[]
            {
                RegValue(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                RegValue(RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                RegValue(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            };
            var libs = new List<string>();
            foreach (var r in roots)
            {
                if (string.IsNullOrEmpty(r))
                {
                    continue;
                }
                string root = r.Replace('/', '\\');
                libs.Add(root);
                try
                {
                    string vdf = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                    {
                        libs.AddRange(ParseLibraryFolders(File.ReadAllText(vdf, Encoding.UTF8)).Select(p => p.Replace('/', '\\')));
                    }
                }
                catch (Exception)
                {
                    // 読めなければ飛ばす
                }
            }
            return Unique(libs.Where(IsLocal));
        }

        private static List<string> OculusLibraries()
        {
            var result = new List<string>();
            try
            {
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
                using (var libs = root.OpenSubKey(@"Software\Oculus VR, LLC\Oculus\Libraries"))
                {
                    if (libs != null)
                    {
                        foreach (var name in libs.GetSubKeyNames())
                        {
                            using (var k = libs.OpenSubKey(name))
                            {
                                if (k?.GetValue("OriginalPath") is string p && IsLocal(p))
                                {
                                    result.Add(p);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 無ければ空
            }
            return Unique(result);
        }

        [DataContract]
        private sealed class BsmConfig
        {
            [DataMember(Name = "installation-folder")] public string InstallationFolder = null;
            [DataMember(Name = "last-version-launched")] public BsmLastVersion LastVersionLaunched = null;
        }

        [DataContract]
        private sealed class BsmLastVersion
        {
            [DataMember(Name = "BSVersion")] public string BSVersion = null;
            [DataMember(Name = "path")] public string Path = null;
        }

        private static BsmConfig ReadBsmConfig()
        {
            try
            {
                string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string file = System.IO.Path.Combine(appdata, "bs-manager", "config.json");
                if (!IsLocal(file) || !File.Exists(file))
                {
                    return null;
                }
                var serializer = new DataContractJsonSerializer(typeof(BsmConfig));
                using (var stream = new MemoryStream(File.ReadAllBytes(file)))
                {
                    return serializer.ReadObject(stream) as BsmConfig;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<BeatSaberLocation> BsManagerInstalls()
        {
            var config = ReadBsmConfig();
            var roots = new List<string>();
            if (IsLocal(config?.InstallationFolder))
            {
                roots.Add(System.IO.Path.Combine(config.InstallationFolder, "BSManager"));
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (IsLocal(home))
            {
                roots.Add(System.IO.Path.Combine(home, "BSManager"));
            }

            // 最後に起動した版: path は BSManager の場所を移すと古いまま残るので、版のフォルダ名だけ見る
            var lastNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var last = config?.LastVersionLaunched;
            if (!string.IsNullOrEmpty(last?.Path))
            {
                lastNames.Add(System.IO.Path.GetFileName(last.Path.TrimEnd('\\', '/')));
            }
            if (!string.IsNullOrEmpty(last?.BSVersion))
            {
                lastNames.Add(last.BSVersion);
            }

            return Unique(roots)
                .SelectMany(r => Instances(System.IO.Path.Combine(r, BsmInstances)))
                .Select(p => new BeatSaberLocation
                {
                    Path = p,
                    Source = "bsmanager",
                    LastLaunched = lastNames.Contains(System.IO.Path.GetFileName(p)),
                })
                .OrderBy(x => x.LastLaunched ? 0 : 1)
                .ToList();
        }

        /// <summary>
        /// 見つかった Beat Saber のフォルダ（Beat Saber.exe があることを確かめた物だけ）。
        /// BSManager を使っている人はそこから起動しているはずなので、BSManager の版を先に（最後に起動した版が先頭）
        /// </summary>
        internal static List<BeatSaberLocation> FindInstalls()
        {
            var candidates = new List<BeatSaberLocation>();
            candidates.AddRange(BsManagerInstalls());
            foreach (var lib in SteamLibraries())
            {
                candidates.Add(new BeatSaberLocation { Path = System.IO.Path.Combine(new[] { lib }.Concat(SteamSub).ToArray()), Source = "steam" });
            }
            foreach (var lib in OculusLibraries())
            {
                candidates.Add(new BeatSaberLocation { Path = System.IO.Path.Combine(new[] { lib }.Concat(OculusSub).ToArray()), Source = "oculus" });
            }
            candidates.Add(new BeatSaberLocation { Path = @"C:\Program Files (x86)\Steam\steamapps\common\Beat Saber", Source = "steam" });
            candidates.Add(new BeatSaberLocation { Path = @"C:\Program Files\Oculus\Software\Software\hyperbolic-magnetism-beat-saber", Source = "oculus" });

            var seen = new HashSet<string>();
            var result = new List<BeatSaberLocation>();
            foreach (var c in candidates)
            {
                if (IsRoot(c.Path) && seen.Add(Norm(c.Path)))
                {
                    c.Path = System.IO.Path.GetFullPath(c.Path);
                    result.Add(c);
                }
            }
            return result;
        }
    }
}
