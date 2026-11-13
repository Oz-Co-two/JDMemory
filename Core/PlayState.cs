using System;
using System.Collections.Generic;

namespace JDMemory.Core
{
    /// <summary>今回のプレイ（ソロ・練習）の情報</summary>
    internal sealed class PlayContext
    {
        public MapSelection Selection;
        public bool Practice;

        /// <summary>JDMemory が適用する JD（オフのときは null）</summary>
        public float? DesiredJD;

        /// <summary>ゲーム側でレベルの準備が始まったか（譜面が一致したときだけ true）</summary>
        public bool Initialized;
        public float Speed = 1f;

        public bool Applied;
        public float ActualJD;
        public float ActualNJS;

        public int MultipliedScore;
        public int MaxMultipliedScore;
    }

    /// <summary>
    /// 選択中の譜面・今回の値・プレイ中の情報をまとめて持つ。
    /// すべて Unity のメインスレッドから呼ばれる前提。
    /// </summary>
    internal static class PlayState
    {
        private static readonly Dictionary<string, float> PendingJD = new Dictionary<string, float>();

        internal static MapSelection Selected { get; private set; }
        internal static PlayContext Current { get; private set; }

        internal static event Action SelectionChanged;
        internal static event Action HistoryChanged;

        internal static void SetSelected(MapSelection selection)
        {
            if (selection == null)
            {
                return;
            }
            Selected = selection;
            SelectionChanged?.Invoke();
        }

        /// <summary>今回の JD がどこから来たか</summary>
        internal enum JDSource
        {
            /// <summary>この譜面用にスライダーで調整した値</summary>
            Adjusted,
            /// <summary>この譜面で前回遊んだ値</summary>
            History,
            /// <summary>デフォルト設定の値（未プレイの譜面）</summary>
            Default,
            /// <summary>譜面の初期値（未プレイでデフォルトがオフ）</summary>
            MapOriginal,
        }

        internal static JDSource GetJDSource(MapSelection selection)
        {
            if (PendingJD.ContainsKey(selection.KeyString))
            {
                return JDSource.Adjusted;
            }
            if (HistoryStore.Instance?.Get(selection.KeyString)?.LastJD != null)
            {
                return JDSource.History;
            }
            return PluginConfig.Instance.UseDefault ? JDSource.Default : JDSource.MapOriginal;
        }

        /// <summary>この譜面で今回使う JD</summary>
        internal static float GetJD(MapSelection selection)
        {
            switch (GetJDSource(selection))
            {
                case JDSource.Adjusted:
                    return PendingJD[selection.KeyString];
                case JDSource.History:
                    return HistoryStore.Instance.Get(selection.KeyString).LastJD.Value;
                case JDSource.Default:
                    return DefaultJD(selection);
                default:
                    return selection.OriginalJD;
            }
        }

        /// <summary>デフォルト設定をこの譜面に当てはめた JD（RT 基準なら NJS から換算）</summary>
        internal static float DefaultJD(MapSelection selection)
        {
            var config = PluginConfig.Instance;
            return config.DefaultMode == 0
                ? config.DefaultJD
                : JumpMath.ToJumpDistance(config.DefaultRT, selection.Njs);
        }

        internal static void SetJD(MapSelection selection, float jumpDistance)
        {
            PendingJD[selection.KeyString] = jumpDistance;
        }

        /// <summary>ソロ・練習でプレイボタンが押された（リザルトからのリトライも含む）</summary>
        internal static void BeginPlay(bool practice)
        {
            var selection = Selected;
            if (selection == null)
            {
                Current = null;
                return;
            }

            float? desired = null;
            if (PluginConfig.Instance.Enabled)
            {
                desired = Math.Max(GetJD(selection), selection.MinJD);
            }

            Current = new PlayContext
            {
                Selection = selection,
                Practice = practice,
                DesiredJD = desired,
            };
            Plugin.Log.Debug($"BeginPlay {selection.KeyString} practice={practice} desired={desired}");
        }

        /// <summary>ゲーム側でレベルの準備が始まった</summary>
        internal static void OnStandardLevelInit(StandardLevelScenesTransitionSetupDataSO setupData)
        {
            var current = Current;
            if (current == null)
            {
                return;
            }

            string key = MapSelection.MakeKeyString(setupData.beatmapKey);
            if (key != current.Selection.KeyString)
            {
                // リプレイ再生など、プレイボタン以外から始まったもの
                Plugin.Log.Debug($"Level init for another map ({key}); ignoring");
                Current = null;
                return;
            }

            current.Practice = setupData.practiceSettings != null;
            current.Speed = setupData.practiceSettings != null
                ? setupData.practiceSettings.songSpeedMul
                : setupData.gameplayModifiers?.songSpeedMul ?? 1f;
            current.Initialized = true;
        }

        /// <summary>マルチ・キャンペーンなど対象外のレベルが始まった</summary>
        internal static void OnOtherLevelInit()
        {
            Current = null;
        }

        internal static void OnScoreChanged(int multipliedScore, int maxMultipliedScore)
        {
            var current = Current;
            if (current == null || !current.Initialized)
            {
                return;
            }
            current.MultipliedScore = multipliedScore;
            current.MaxMultipliedScore = maxMultipliedScore;
        }

        internal static void OnLevelFinished(LevelCompletionResults results)
        {
            var current = Current;
            if (current == null || !current.Initialized || results == null)
            {
                return;
            }

            string result;
            if (results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Cleared)
            {
                result = "Cleared";
            }
            else if (results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed)
            {
                result = "Failed";
            }
            else if (results.levelEndAction == LevelCompletionResults.LevelEndAction.Restart)
            {
                result = "Restart";
            }
            else
            {
                result = "Quit";
            }

            bool restart = results.levelEndAction == LevelCompletionResults.LevelEndAction.Restart;

            // ノーツを1つも処理していない（すぐやめた・すぐやり直した）ものは残さない
            if (current.MaxMultipliedScore <= 0 && result != "Cleared")
            {
                Plugin.Log.Debug("No notes played; not recording");
                ResetAfterFinish(current, restart);
                return;
            }

            var selection = current.Selection;
            float njs = current.ActualNJS > 0.01f ? current.ActualNJS : selection.Njs;
            float jd = current.ActualJD > 0.01f ? current.ActualJD : (current.DesiredJD ?? selection.OriginalJD);

            var entry = new HistoryEntry
            {
                Time = DateTime.Now,
                JD = (float)Math.Round(jd, 2),
                RT = (float)Math.Round(JumpMath.ToReactionTime(jd, njs), 0),
                NJS = njs,
                OriginalJD = (float)Math.Round(selection.OriginalJD, 2),
                Speed = current.Speed,
                Practice = current.Practice,
                Applied = current.Applied,
                Result = result,
                Accuracy = current.MaxMultipliedScore > 0
                    ? (float?)Math.Round(100.0 * results.multipliedScore / current.MaxMultipliedScore, 2)
                    : null,
                Misses = results.missedCount + results.badCutsCount,
                EndTime = results.endSongTime,
                SongDuration = selection.Level != null ? selection.Level.songDuration : 0f,
            };

            HistoryStore.Instance?.Add(selection, entry);
            PendingJD.Remove(selection.KeyString);
            Plugin.Log.Info($"Recorded {selection.SongName} [{selection.DifficultyLabel}] JD {entry.JD} RT {entry.RT} {result} {entry.Accuracy}%");

            ResetAfterFinish(current, restart);
            HistoryChanged?.Invoke();
        }

        private static void ResetAfterFinish(PlayContext current, bool restart)
        {
            if (restart)
            {
                // やり直しはそのまま同じ設定で続ける
                current.MultipliedScore = 0;
                current.MaxMultipliedScore = 0;
                current.Applied = false;
                current.ActualJD = 0f;
            }
            else
            {
                Current = null;
            }
        }
    }
}
