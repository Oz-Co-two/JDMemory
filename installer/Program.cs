using System;
using System.Net;
using System.Windows.Forms;

namespace JDMemoryInstaller
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // GitHub は TLS 1.2 以上が必要（古い Windows の既定に任せない）
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            // 利用者が SmartScreen で「実行」を選んで開いた後なので、自分の exe の「インターネットから来た」印を外す
            // （次に開く時＝更新の時などは警告が出ない。NLM・blnext と同じ考え方）
            MarkOfTheWeb.Remove(Application.ExecutablePath);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception e)
            {
                MessageBox.Show("インストーラーを開けませんでした。\n\n" + e.Message, "JDMemory インストーラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
