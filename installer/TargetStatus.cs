using System;
using System.Diagnostics;
using System.IO;

namespace JDMemoryInstaller
{
    /// <summary>入れる先（Beat Saber のフォルダ）の状態</summary>
    internal sealed class TargetStatus
    {
        public BeatSaberLocation Location;

        /// <summary>ゲームの版（BeatSaberVersion.txt の「_」より前。例: 1.40.8）。読めなければ null</summary>
        public string GameVersion;

        /// <summary>MOD を読み込む仕組み（BSIPA）が入っているか</summary>
        public bool HasBsipa;
        public bool HasBsml;
        public bool HasSiraUtil;

        /// <summary>入っている JDMemory の版（例: 0.1.0）。入っていなければ null</summary>
        public string InstalledVersion;

        /// <summary>ゲームの中で準備され、次の起動で入れ替わる予定の版（IPA\Pending）。無ければ null</summary>
        public string PendingVersion;

        /// <summary>履歴（UserData\JDMemory）があるか</summary>
        public bool HasHistory;

        public string Path => Location.Path;
        public string PluginsDir => System.IO.Path.Combine(Path, "Plugins");
        public string DllPath => System.IO.Path.Combine(PluginsDir, Installer.DllName);
        public string PendingDllPath => System.IO.Path.Combine(Path, "IPA", "Pending", "Plugins", Installer.DllName);
        public string HistoryDir => System.IO.Path.Combine(Path, "UserData", "JDMemory");
        public string ConfigFile => System.IO.Path.Combine(Path, "UserData", "JDMemory.json");

        public bool HasRequiredMods => HasBsml && HasSiraUtil;

        internal static TargetStatus Inspect(BeatSaberLocation location)
        {
            var status = new TargetStatus { Location = location };
            string root = location.Path;
            status.GameVersion = ReadGameVersion(root);
            status.HasBsipa = File.Exists(System.IO.Path.Combine(root, "Beat Saber_Data", "Managed", "IPA.Loader.dll"));
            status.HasBsml = File.Exists(System.IO.Path.Combine(root, "Plugins", "BSML.dll"));
            status.HasSiraUtil = File.Exists(System.IO.Path.Combine(root, "Plugins", "SiraUtil.dll"));
            status.InstalledVersion = ReadInstalledVersion(status.DllPath);
            status.PendingVersion = ReadInstalledVersion(status.PendingDllPath);
            status.HasHistory = Directory.Exists(status.HistoryDir);
            return status;
        }

        private static string ReadGameVersion(string root)
        {
            try
            {
                string file = System.IO.Path.Combine(root, "BeatSaberVersion.txt");
                if (!File.Exists(file))
                {
                    return null;
                }
                string text = File.ReadAllText(file).Trim();
                int cut = text.IndexOf('_');
                return cut > 0 ? text.Substring(0, cut) : text;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ReadInstalledVersion(string dll)
        {
            try
            {
                if (!File.Exists(dll))
                {
                    return null;
                }
                var v = ModPackage.ParseVersion(FileVersionInfo.GetVersionInfo(dll).FileVersion);
                return v != null ? v.ToString(3) : "不明";
            }
            catch (Exception)
            {
                return "不明";
            }
        }

        internal string SourceLabel
        {
            get
            {
                switch (Location.Source)
                {
                    case "bsmanager": return Location.LastLaunched ? "BSManager（最後に起動）" : "BSManager";
                    case "steam": return "Steam";
                    case "oculus": return "Meta (Oculus)";
                    default: return "選んだフォルダ";
                }
            }
        }

        internal string RequiredModsLabel
        {
            get
            {
                if (!HasBsipa)
                {
                    return "MOD なし";
                }
                if (HasRequiredMods)
                {
                    return "OK";
                }
                return "不足: " + (HasBsml ? "" : "BSML ") + (HasSiraUtil ? "" : "SiraUtil");
            }
        }
    }
}
