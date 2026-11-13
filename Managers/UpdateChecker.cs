using System;
using System.IO;
using System.Text;
using IPA.Utilities;
using JDMemory.Core;
using UnityEngine.Networking;
using Zenject;

namespace JDMemory.Managers
{
    internal enum UpdateStage
    {
        /// <summary>知らせる物は無い</summary>
        None,
        /// <summary>新しい版がある（自動では準備しない・できない）</summary>
        Available,
        /// <summary>取得している</summary>
        Downloading,
        /// <summary>IPA\Pending に置いた。次の起動で BSIPA が入れ替える</summary>
        Staged,
        /// <summary>取得・照合に失敗した</summary>
        Failed,
        /// <summary>準備を取り消した</summary>
        Cancelled,
    }

    /// <summary>新しい版の状態（タブに出す）</summary>
    internal static class UpdateInfo
    {
        internal static UpdateStage Stage { get; private set; }

        /// <summary>新しい版（例: v0.2.0）</summary>
        internal static string NewerVersion { get; private set; }

        /// <summary>自動で準備しない理由・失敗の理由など、補足の文</summary>
        internal static string Note { get; private set; }

        internal static event Action Changed;

        internal static void Set(UpdateStage stage, string version, string note = null)
        {
            Stage = stage;
            NewerVersion = version;
            Note = note;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// ゲームの起動中に1回だけ、GitHub の最新リリースの update.json を読んで新しい版があるか確かめ、
    /// 設定がオンなら取得・照合して IPA\Pending\Plugins\JDMemory.dll に置く（判定・照合・配置は Core.UpdatePackage）。
    /// BSIPA は次の起動の最初（MOD を読む前）に IPA\Pending の中身をゲームのフォルダへ上書きでコピーして Pending を消すので、
    /// 次にゲームを起動した時に新しい版へ入れ替わる（IPA.Injector の Updates.InstallPendingModUpdates）。
    /// 通信の失敗は、新しい版があると分かる前なら何も出さない。
    /// </summary>
    internal sealed class UpdateChecker : IInitializable
    {
        private static bool _checkedThisSession;

        private static string PendingDir => Path.Combine(UnityGame.InstallPath, "IPA", "Pending");
        private static string PendingDll => Path.Combine(PendingDir, "Plugins", "JDMemory.dll");
        private static string TmpDir => Path.Combine(UnityGame.UserDataPath, "JDMemory");

        public void Initialize()
        {
            if (_checkedThisSession || PluginConfig.Instance == null || !PluginConfig.Instance.CheckUpdates)
            {
                return;
            }
            _checkedThisSession = true;
            Send(UpdatePackage.UpdateUrl, 15, OnManifest);
        }

        private static void Send(string url, int timeoutSeconds, Action<UnityWebRequest> onDone)
        {
            try
            {
                var request = UnityWebRequest.Get(url);
                request.timeout = timeoutSeconds;
                request.SetRequestHeader("User-Agent", "JDMemory/" + CurrentVersion().ToString(3));
                var operation = request.SendWebRequest();
                operation.completed += _ =>
                {
                    try
                    {
                        onDone(request);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.Warn($"Update: {e.Message}");
                    }
                    finally
                    {
                        request.Dispose();
                    }
                };
            }
            catch (Exception e)
            {
                Plugin.Log.Debug($"Update request failed to start: {e.Message}");
            }
        }

        private static void OnManifest(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.Debug($"Update check: {request.responseCode} {request.error}");
                return;
            }
            byte[] data = request.downloadHandler?.data;
            if (data == null || data.Length == 0 || data.Length > UpdatePackage.MaxManifest)
            {
                return;
            }

            var decision = UpdatePackage.Decide(Encoding.UTF8.GetString(data), CurrentVersion(),
                CurrentGameVersion(), PluginConfig.Instance.AutoUpdate);
            if (decision == null || !decision.Newer)
            {
                return;
            }
            Plugin.Log.Info($"New version available: {decision.Label} (current v{CurrentVersion().ToString(3)})");
            if (!decision.Download)
            {
                UpdateInfo.Set(UpdateStage.Available, decision.Label, decision.Note);
                return;
            }

            UpdateInfo.Set(UpdateStage.Downloading, decision.Label);
            Send(decision.ZipUrl, 60, r => OnZip(r, decision));
        }

        private static void OnZip(UnityWebRequest request, UpdateDecision decision)
        {
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new InvalidDataException($"取得できませんでした（{request.responseCode}）");
                }
                byte[] dll = UpdatePackage.ExtractDll(request.downloadHandler?.data, decision.Sha256, decision.Size);
                UpdatePackage.Stage(dll, decision.Version, TmpDir, PendingDll);
                Plugin.Log.Info($"Staged {decision.Label} to {PendingDll}");
                UpdateInfo.Set(UpdateStage.Staged, decision.Label);
            }
            catch (Exception e)
            {
                Plugin.Log.Warn($"Update download failed: {e.Message}");
                UpdateInfo.Set(UpdateStage.Failed, decision.Label, e.Message);
            }
        }

        /// <summary>準備した新しい版を取り消す</summary>
        internal static void CancelStaged()
        {
            try
            {
                UpdatePackage.Cancel(PendingDll, PendingDir);
                UpdateInfo.Set(UpdateStage.Cancelled, UpdateInfo.NewerVersion);
            }
            catch (Exception e)
            {
                Plugin.Log.Warn($"Cancel update failed: {e.Message}");
            }
        }

        internal static Version CurrentVersion()
        {
            var v = typeof(Plugin).Assembly.GetName().Version;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        /// <summary>今のゲームの版（BeatSaberVersion.txt の「_」より前。例: 1.40.8）。読めなければ null</summary>
        private static string CurrentGameVersion()
        {
            try
            {
                string file = Path.Combine(UnityGame.InstallPath, "BeatSaberVersion.txt");
                string text = File.Exists(file) ? File.ReadAllText(file).Trim() : UnityGame.GameVersion.ToString();
                int cut = text.IndexOf('_');
                return cut > 0 ? text.Substring(0, cut) : text;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
