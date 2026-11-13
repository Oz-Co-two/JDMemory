using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace JDMemoryInstaller
{
    /// <summary>入れる JDMemory（同梱の物か、GitHub から取って照合した物）</summary>
    internal sealed class ModPackage
    {
        /// <summary>版（例: 0.1.0。先頭に v は付けない）</summary>
        public string Version;

        /// <summary>動作を確かめたゲームの版（例: 1.40.8）</summary>
        public string[] GameVersions;

        public byte[] Dll;

        /// <summary>true: このインストーラーに同梱 / false: GitHub から取得</summary>
        public bool Bundled;

        [DataContract]
        private sealed class Manifest
        {
            [DataMember(Name = "version")] public string Version = null;
            [DataMember(Name = "gameVersion")] public string GameVersion = null;
        }

        /// <summary>このインストーラーに同梱した MOD（ビルドの時に中に入れた JDMemory.dll と manifest.json）</summary>
        internal static ModPackage LoadBundled()
        {
            var asm = Assembly.GetExecutingAssembly();
            byte[] dll = ReadResource(asm, "JDMemory.dll");
            byte[] manifestBytes = ReadResource(asm, "manifest.json");
            Manifest manifest;
            using (var stream = new MemoryStream(manifestBytes))
            {
                manifest = (Manifest)new DataContractJsonSerializer(typeof(Manifest)).ReadObject(stream);
            }
            return new ModPackage
            {
                Version = manifest.Version,
                GameVersions = string.IsNullOrEmpty(manifest.GameVersion) ? new string[0] : new[] { manifest.GameVersion },
                Dll = dll,
                Bundled = true,
            };
        }

        private static byte[] ReadResource(Assembly asm, string name)
        {
            using (var stream = asm.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException($"インストーラーの中に {name} がありません");
                }
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        internal bool SupportsGame(string gameVersion)
        {
            if (string.IsNullOrEmpty(gameVersion) || GameVersions == null)
            {
                return false;
            }
            foreach (var v in GameVersions)
            {
                if (string.Equals(v, gameVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        internal string GameVersionsText => GameVersions == null || GameVersions.Length == 0 ? "不明" : string.Join(", ", GameVersions);

        /// <summary>「v1.2.3」「1.2.3」「1.2.3.0」を Version（3桁）にする。読めなければ null</summary>
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
            if (parts.Length < 3 || parts.Length > 4)
            {
                return null;
            }
            return int.TryParse(parts[0], out int a) && int.TryParse(parts[1], out int b) && int.TryParse(parts[2], out int c)
                ? new Version(a, b, c)
                : null;
        }
    }
}
