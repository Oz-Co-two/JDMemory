using System;
using System.Runtime.InteropServices;

namespace JDMemoryInstaller
{
    /// <summary>
    /// 「インターネットから来たファイル」の印（Zone.Identifier。NTFS の代替データストリーム）を外す。
    /// NLM・blnext の app.py の _unblock_dist_folder() と同じ考え方で、外すのは自分の物（このインストーラーの exe と、
    /// インストーラーが入れた Plugins\JDMemory.dll）だけ。ほかのファイルの印には触らない。
    ///  - インストーラーの exe: 利用者が SmartScreen で「詳細情報 → 実行」を選んで開いた後に外すので、
    ///    次に開く時（更新の時など）は警告が出ない
    ///  - JDMemory.dll: インストーラーが書いた物には印は付かないが、念のため外す
    /// .NET Framework の File.Delete は「:」を含むパスを受け付けないので、Windows の DeleteFileW を直接呼ぶ。
    /// 失敗しても（印が無い・消せない）何もしない。
    /// </summary>
    internal static class MarkOfTheWeb
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFileW(string path);

        internal static void Remove(string file)
        {
            try
            {
                if (!string.IsNullOrEmpty(file))
                {
                    DeleteFileW(file + ":Zone.Identifier");
                }
            }
            catch (Exception)
            {
                // 印が外せなくても、インストールは妨げない
            }
        }
    }
}
