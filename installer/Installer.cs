using System.Diagnostics;
using System.IO;

namespace JDMemoryInstaller
{
    /// <summary>入れる・外す。触るのは Plugins\JDMemory.dll と、頼まれた時だけ JDMemory の履歴と設定</summary>
    internal static class Installer
    {
        internal const string DllName = "JDMemory.dll";

        internal static bool IsGameRunning()
        {
            var processes = Process.GetProcessesByName("Beat Saber");
            foreach (var p in processes)
            {
                p.Dispose();
            }
            return processes.Length > 0;
        }

        /// <summary>
        /// Plugins\JDMemory.dll を入れる（あれば置き換える）。一時ファイルに書いてから入れ替えるので、途中で失敗しても壊れない。
        /// ゲームの中で準備した版（IPA\Pending\Plugins\JDMemory.dll）が残っていると、次の起動で BSIPA がそれで上書きして
        /// 今入れた版より古くなることがあるので、先に消す。
        /// </summary>
        internal static void Install(TargetStatus target, ModPackage package)
        {
            RemovePending(target);
            Directory.CreateDirectory(target.PluginsDir);
            string dest = target.DllPath;
            string tmp = dest + ".jdmemory-tmp";
            File.WriteAllBytes(tmp, package.Dll);
            try
            {
                if (File.Exists(dest))
                {
                    File.Replace(tmp, dest, null);
                }
                else
                {
                    File.Move(tmp, dest);
                }
            }
            finally
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            // インストーラーが書いた物には「インターネットから来た」印は付かないが、念のため外す
            MarkOfTheWeb.Remove(dest);
        }

        /// <summary>JDMemory を外す。deleteData の時だけ履歴（UserData\JDMemory）と設定（UserData\JDMemory.json）も消す</summary>
        internal static void Uninstall(TargetStatus target, bool deleteData)
        {
            if (File.Exists(target.DllPath))
            {
                File.Delete(target.DllPath);
            }
            // BSIPA が次の起動で入れる予定の物（ゲームの中で準備した新しい版）も外す
            RemovePending(target);
            if (deleteData)
            {
                if (Directory.Exists(target.HistoryDir))
                {
                    Directory.Delete(target.HistoryDir, true);
                }
                if (File.Exists(target.ConfigFile))
                {
                    File.Delete(target.ConfigFile);
                }
            }
        }

        /// <summary>IPA\Pending\Plugins\JDMemory.dll を消し、空になったフォルダだけ片付ける（ほかの MOD の物には触らない）</summary>
        private static void RemovePending(TargetStatus target)
        {
            if (File.Exists(target.PendingDllPath))
            {
                File.Delete(target.PendingDllPath);
            }
            DeleteIfEmpty(Path.GetDirectoryName(target.PendingDllPath));
            DeleteIfEmpty(Path.Combine(target.Path, "IPA", "Pending"));
        }

        private static void DeleteIfEmpty(string dir)
        {
            if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
            {
                Directory.Delete(dir);
            }
        }
    }
}
