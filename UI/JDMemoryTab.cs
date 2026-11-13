using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.GameplaySetup;
using JDMemory.Core;
using JDMemory.Managers;
using Zenject;

namespace JDMemory.UI
{
    /// <summary>
    /// 曲選択画面の左パネル「MODS」タブに出す JDMemory の画面。
    /// </summary>
    internal sealed class JDMemoryTab : IInitializable, IDisposable, INotifyPropertyChanged
    {
        internal const string TabName = "JDMemory";
        private const string ResourcePath = "JDMemory.UI.JDMemoryTab.bsml";

        public event PropertyChangedEventHandler PropertyChanged;

        [UIComponent("jd_slider")] private SliderSetting _jdSlider = null;
        [UIComponent("rt_slider")] private SliderSetting _rtSlider = null;

        private static PluginConfig Config => PluginConfig.Instance;
        private static MapSelection Selection => PlayState.Selected;
        private static MapHistory History => Selection != null ? HistoryStore.Instance?.Get(Selection.KeyString) : null;

        public void Initialize()
        {
            GameplaySetup.Instance.AddTab(TabName, ResourcePath, this, MenuType.Solo);
            PlayState.SelectionChanged += Refresh;
            PlayState.HistoryChanged += Refresh;
            UpdateInfo.Changed += OnUpdateInfoChanged;
        }

        public void Dispose()
        {
            PlayState.SelectionChanged -= Refresh;
            PlayState.HistoryChanged -= Refresh;
            UpdateInfo.Changed -= OnUpdateInfoChanged;
            if (GameplaySetup.Instance != null)
            {
                GameplaySetup.Instance.RemoveTab(TabName);
            }
        }

        private void Refresh()
        {
            Notify(nameof(MapInfo));
            Notify(nameof(WarningText));
            Notify(nameof(JdValue));
            Notify(nameof(RtValue));
            Notify(nameof(SummaryText));
            Notify(nameof(HistoryText));
            Notify(nameof(HasMap));
            Notify(nameof(HasHistory));
            Notify(nameof(HasBest));
            Notify(nameof(CanApplyDefault));
            Notify(nameof(HasWarning));
        }

        [UIAction("#post-parse")]
        private void PostParse()
        {
            try
            {
                // BSML のスライダーは右寄せ・幅 52 固定なので、割り当てた幅いっぱいに広げる
                FillSlider(_jdSlider);
                FillSlider(_rtSlider);
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }

        /// <summary>
        /// BSML のスライダーは「右端に幅 52 で置く」作り。項目名は使わず、
        /// スライダー部品に割り当てた幅（BSML の pref-width）いっぱいにバーを広げる。
        /// </summary>
        private static void FillSlider(SliderSetting setting)
        {
            if (setting == null || setting.Slider == null)
            {
                return;
            }
            var rect = setting.Slider.transform as UnityEngine.RectTransform;
            if (rect == null)
            {
                return;
            }
            rect.anchorMin = new UnityEngine.Vector2(0f, rect.anchorMin.y);
            rect.anchorMax = new UnityEngine.Vector2(1f, rect.anchorMax.y);
            rect.pivot = new UnityEngine.Vector2(0.5f, rect.pivot.y);
            rect.sizeDelta = new UnityEngine.Vector2(0f, rect.sizeDelta.y);
            rect.anchoredPosition = new UnityEngine.Vector2(0f, rect.anchoredPosition.y);
        }


        private void Notify(string propertyName)
        {
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }

        // ---- 有効・無効 -------------------------------------------------------

        [UIValue("enabled")]
        public bool Enabled
        {
            get => Config.Enabled;
            set
            {
                Config.Enabled = value;
                Notify(nameof(WarningText));
                Notify(nameof(HasWarning));
            }
        }

        // ---- 譜面の情報 ------------------------------------------------------

        [UIValue("map_info")]
        public string MapInfo
        {
            get
            {
                var s = Selection;
                if (s == null)
                {
                    return "<color=#aaaaaa>曲を選ぶと、この譜面の履歴が表示されます</color>";
                }
                return $"<b>{Escape(Truncate(s.SongName, 20))}</b> <color=#ffcc66>{s.DifficultyLabel}</color>"
                     + $"  NJS {s.Njs:0.#}  初期値 JD {s.OriginalJD:0.0#} / {s.OriginalRT:0}ms";
            }
        }

        /// <summary>注意書きがあるときだけ、その行を表示する（空行を作らない）</summary>
        [UIValue("has_warning")] public bool HasWarning => WarningText.Length > 0;

        [UIValue("warning_text")]
        public string WarningText
        {
            get
            {
                if (!Config.Enabled)
                {
                    return "<color=#aaaaaa>オフ：JDは変えずに、履歴だけ記録します</color>";
                }
                if (IsJDFixerEnabled())
                {
                    return "<color=#ff9966>JDFixer もオンです（JDMemory の値が優先されます）</color>";
                }
                return "";
            }
        }

        // ---- JD / RT スライダー ---------------------------------------------

        [UIValue("jd_min")] public float JdMin => Config.MinJD;
        [UIValue("jd_max")] public float JdMax => Config.MaxJD;
        [UIValue("rt_min")] public float RtMin => Config.MinRT;
        [UIValue("rt_max")] public float RtMax => Config.MaxRT;

        [UIValue("jd_value")]
        public float JdValue
        {
            get => Selection != null ? PlayState.GetJD(Selection) : Config.MinJD;
            set
            {
                var s = Selection;
                if (s == null || Math.Abs(value - PlayState.GetJD(s)) < 0.001f)
                {
                    return;
                }
                PlayState.SetJD(s, value);
                Notify(nameof(RtValue));
                Notify(nameof(SummaryText));
            }
        }

        [UIValue("rt_value")]
        public float RtValue
        {
            get => Selection != null ? JumpMath.ToReactionTime(PlayState.GetJD(Selection), Selection.Njs) : Config.MinRT;
            set
            {
                var s = Selection;
                if (s == null || Math.Abs(value - JumpMath.ToReactionTime(PlayState.GetJD(s), s.Njs)) < 0.01f)
                {
                    return;
                }
                PlayState.SetJD(s, JumpMath.ToJumpDistance(value, s.Njs));
                Notify(nameof(JdValue));
                Notify(nameof(SummaryText));
            }
        }

        [UIAction("jd_formatter")]
        private string JdFormatter(float value) => value.ToString("0.0#");

        [UIAction("rt_formatter")]
        private string RtFormatter(float value) => value.ToString("0") + " ms";

        // ---- 一定量を足し引きするボタン ----------------------------------------

        [UIValue("jd_step")] public float JdStep => Config.JDStep;
        [UIValue("rt_step")] public float RtStep => Config.RTStep;

        [UIAction("jd_minus_1")] private void JdMinus1() => AddJD(-1f);
        [UIAction("jd_minus_05")] private void JdMinus05() => AddJD(-0.5f);
        [UIAction("jd_plus_05")] private void JdPlus05() => AddJD(0.5f);
        [UIAction("jd_plus_1")] private void JdPlus1() => AddJD(1f);

        [UIAction("rt_minus_100")] private void RtMinus100() => AddRT(-100f);
        [UIAction("rt_minus_10")] private void RtMinus10() => AddRT(-10f);
        [UIAction("rt_plus_10")] private void RtPlus10() => AddRT(10f);
        [UIAction("rt_plus_100")] private void RtPlus100() => AddRT(100f);

        /// <summary>JD を足し引きし、JD スライダーの刻みに揃える</summary>
        private void AddJD(float delta)
        {
            var s = Selection;
            if (s == null)
            {
                return;
            }
            float jd = Snap(PlayState.GetJD(s) + delta, Config.JDStep);
            jd = Clamp(jd, Config.MinJD, Config.MaxJD);
            SetJD(Math.Max(jd, s.MinJD));
        }

        /// <summary>RT を足し引きし、RT スライダーの刻みに揃える</summary>
        private void AddRT(float delta)
        {
            var s = Selection;
            if (s == null)
            {
                return;
            }
            float rt = Snap(JumpMath.ToReactionTime(PlayState.GetJD(s), s.Njs) + delta, Config.RTStep);
            rt = Clamp(rt, Config.MinRT, Config.MaxRT);
            SetJD(Math.Max(JumpMath.ToJumpDistance(rt, s.Njs), s.MinJD));
        }

        private static float Snap(float value, float step)
        {
            return step > 0f ? (float)(Math.Round(value / step) * step) : value;
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Min(max, Math.Max(min, value));
        }

        // ---- ボタン ----------------------------------------------------------

        [UIValue("has_map")] public bool HasMap => Selection != null;
        [UIValue("has_history")] public bool HasHistory => History?.Entries.Count > 0;
        [UIValue("has_best")] public bool HasBest => History?.Best() != null;

        [UIAction("use_last")]
        private void UseLast()
        {
            var last = History?.Entries.LastOrDefault();
            if (last != null)
            {
                SetJD(last.JD);
            }
        }

        [UIAction("use_best")]
        private void UseBest()
        {
            var best = History?.Best();
            if (best != null)
            {
                SetJD(best.JD);
            }
        }

        [UIAction("use_map_default")]
        private void UseMapDefault()
        {
            if (Selection != null)
            {
                SetJD(Selection.OriginalJD);
            }
        }

        private void SetJD(float jumpDistance)
        {
            var s = Selection;
            if (s == null)
            {
                return;
            }
            PlayState.SetJD(s, jumpDistance);
            Notify(nameof(JdValue));
            Notify(nameof(RtValue));
            Notify(nameof(SummaryText));
        }

        [UIValue("can_apply_default")] public bool CanApplyDefault => Selection != null && Config.UseDefault;

        [UIAction("apply_default")]
        private void ApplyDefault()
        {
            if (Selection != null)
            {
                SetJD(PlayState.DefaultJD(Selection));
            }
        }

        // ---- 履歴------------------------------------------------------------

        [UIValue("summary_text")]
        public string SummaryText
        {
            get
            {
                var history = History;
                if (Selection == null)
                {
                    return "";
                }
                if (history == null || history.Entries.Count == 0)
                {
                    return "<color=#aaaaaa>未プレイ：</color>" + SourceText(Selection);
                }
                var best = history.Best();
                string bestText = best != null
                    ? $"   最高 <color=#88ff88>{best.Accuracy:0.00}%</color> (JD {best.JD:0.0#})"
                    : "";
                return $"履歴 {history.Entries.Count} 回{bestText}";
            }
        }

        [UIValue("history_text")]
        public string HistoryText
        {
            get
            {
                var history = History;
                if (history == null || history.Entries.Count == 0)
                {
                    return "";
                }
                int lines = Math.Max(1, Config.HistoryLines);
                var sb = new StringBuilder();
                foreach (var e in history.Entries.AsEnumerable().Reverse().Take(lines))
                {
                    if (sb.Length > 0)
                    {
                        sb.Append('\n');
                    }
                    sb.Append(FormatEntry(e));
                }
                return sb.ToString();
            }
        }

        private static string SourceText(MapSelection s)
        {
            switch (PlayState.GetJDSource(s))
            {
                case PlayState.JDSource.Adjusted:
                    return "<color=#aaaaaa>この譜面用に調整した値で遊びます</color>";
                case PlayState.JDSource.Default:
                    return Config.DefaultMode == 0
                        ? $"<color=#aaaaaa>デフォルト（JD {Config.DefaultJD:0.0#}）で遊びます</color>"
                        : $"<color=#aaaaaa>デフォルト（RT {Config.DefaultRT:0} ms）で遊びます</color>";
                default:
                    return "<color=#aaaaaa>譜面の初期値で遊びます</color>";
            }
        }

        private static string FormatEntry(HistoryEntry e)
        {
            string acc = e.Accuracy.HasValue ? $"{e.Accuracy:0.00}%" : "-";
            string result;
            switch (e.Result)
            {
                case "Cleared":
                    result = "<color=#88ff88>クリア</color>";
                    break;
                case "Failed":
                    result = "<color=#ff7777>失敗</color>";
                    break;
                case "Restart":
                    result = $"<color=#aaaaaa>やり直し{Progress(e)}</color>";
                    break;
                default:
                    result = $"<color=#aaaaaa>中断{Progress(e)}</color>";
                    break;
            }
            string extra = "";
            if (Math.Abs(e.Speed - 1f) > 0.001f)
            {
                extra += $" x{e.Speed:0.##}";
            }
            if (e.Practice)
            {
                extra += " 練習";
            }
            return $"{e.Time:MM/dd HH:mm}  JD {e.JD:0.0#} / {e.RT:0}ms  {result} {acc} ミス{e.Misses}{extra}";
        }

        private static string Progress(HistoryEntry e)
        {
            if (e.SongDuration <= 0f)
            {
                return "";
            }
            float ratio = Math.Min(1f, Math.Max(0f, e.EndTime / e.SongDuration));
            return $"({ratio * 100f:0}%)";
        }

        // ---- その他の設定 ----------------------------------------------------

        [UIValue("pin_tab_first")]
        public bool PinTabFirst
        {
            get => Config.PinTabFirst;
            set => Config.PinTabFirst = value;
        }

        [UIValue("check_updates")]
        public bool CheckUpdates
        {
            get => Config.CheckUpdates;
            set => Config.CheckUpdates = value;
        }

        /// <summary>タブの一番下に出す今の版（更新で入れ替わったかをゲームの中で確かめられるように）</summary>
        [UIValue("version_text")]
        public string VersionText => "JDMemory v" + UpdateChecker.CurrentVersion().ToString(3);

        [UIValue("auto_update")]
        public bool AutoUpdate
        {
            get => Config.AutoUpdate;
            set => Config.AutoUpdate = value;
        }

        // ---- 新しい版の知らせ --------------------------------------------------

        private void OnUpdateInfoChanged()
        {
            Notify(nameof(UpdateText));
            Notify(nameof(HasUpdate));
            Notify(nameof(CanCancelUpdate));
        }

        [UIValue("has_update")] public bool HasUpdate => UpdateInfo.Stage != UpdateStage.None;

        [UIValue("can_cancel_update")] public bool CanCancelUpdate => UpdateInfo.Stage == UpdateStage.Staged;

        [UIValue("update_text")]
        public string UpdateText
        {
            get
            {
                string v = UpdateInfo.NewerVersion;
                string note = string.IsNullOrEmpty(UpdateInfo.Note) ? "" : $"（{UpdateInfo.Note}）";
                switch (UpdateInfo.Stage)
                {
                    case UpdateStage.Available:
                        return $"<color=#66ccff>新しい版 {v} があります。インストーラーで更新できます{note}</color>";
                    case UpdateStage.Downloading:
                        return $"<color=#66ccff>新しい版 {v} を取得しています…</color>";
                    case UpdateStage.Staged:
                        return $"<color=#88ff88>新しい版 {v} を準備しました。次にゲームを起動すると入れ替わります</color>";
                    case UpdateStage.Failed:
                        return $"<color=#ff9966>新しい版 {v} を取得できませんでした{note}。インストーラーで更新できます</color>";
                    case UpdateStage.Cancelled:
                        return $"<color=#aaaaaa>{v} への入れ替えを取り消しました（インストーラーでいつでも更新できます）</color>";
                    default:
                        return "";
                }
            }
        }

        [UIAction("cancel_update")]
        private void CancelUpdate()
        {
            UpdateChecker.CancelStaged();
        }

        // ---- デフォルト設定（未プレイの譜面） ----------------------------------

        [UIValue("use_default_value")]
        public bool UseDefaultValue
        {
            get => Config.UseDefault;
            set
            {
                Config.UseDefault = value;
                OnDefaultChanged();
            }
        }

        [UIValue("default_mode")]
        public int DefaultMode
        {
            get => Config.DefaultMode;
            set
            {
                Config.DefaultMode = value;
                Notify(nameof(DefaultIsJD));
                Notify(nameof(DefaultIsRT));
                OnDefaultChanged();
            }
        }

        [UIAction("default_mode_formatter")]
        private string DefaultModeFormatter(int value) => value == 0 ? "JD で固定" : "RT で固定";

        [UIValue("default_is_jd")] public bool DefaultIsJD => Config.DefaultMode == 0;
        [UIValue("default_is_rt")] public bool DefaultIsRT => Config.DefaultMode != 0;

        [UIValue("default_jd")]
        public float DefaultJDValue
        {
            get => Config.DefaultJD;
            set
            {
                Config.DefaultJD = value;
                OnDefaultChanged();
            }
        }

        [UIValue("default_rt")]
        public float DefaultRTValue
        {
            get => Config.DefaultRT;
            set
            {
                Config.DefaultRT = value;
                OnDefaultChanged();
            }
        }

        private void OnDefaultChanged()
        {
            // 選択中の譜面が未プレイなら、スライダーの値も変わる
            Notify(nameof(JdValue));
            Notify(nameof(RtValue));
            Notify(nameof(SummaryText));
            Notify(nameof(CanApplyDefault));
        }

        // ---- 補助 ------------------------------------------------------------

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
            {
                return text ?? "";
            }
            return text.Substring(0, max - 1) + "…";
        }

        private static string Escape(string text)
        {
            // リッチテキストのタグとして解釈されないようにする
            return (text ?? "").Replace("<", "<​");
        }

        private static bool IsJDFixerEnabled()
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "JDFixer");
                var type = assembly?.GetType("JDFixer.PluginConfig");
                if (type == null)
                {
                    return false;
                }
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic;
                var instance = type.GetProperty("Instance", flags | BindingFlags.Static)?.GetValue(null);
                if (instance == null)
                {
                    return false;
                }
                var property = type.GetProperty("enabled", flags | BindingFlags.Instance);
                return property != null && property.GetValue(instance) is bool enabled && enabled;
            }
            catch
            {
                return false;
            }
        }
    }
}
