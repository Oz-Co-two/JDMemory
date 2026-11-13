namespace JDMemory
{
    /// <summary>
    /// UserData/JDMemory.json に保存される設定（BSIPA が自動で読み書きする）。
    /// </summary>
    public class PluginConfig
    {
        public static PluginConfig Instance { get; set; }

        /// <summary>JDMemory で JD を変更するか（オフでも履歴の記録は行う）</summary>
        public virtual bool Enabled { get; set; } = true;

        /// <summary>未プレイの譜面にデフォルト値を使うか（オフなら譜面の初期値）</summary>
        public virtual bool UseDefault { get; set; } = false;

        /// <summary>デフォルト値の基準。0: JD / 1: RT</summary>
        public virtual int DefaultMode { get; set; } = 0;

        public virtual float DefaultJD { get; set; } = 18f;
        public virtual float DefaultRT { get; set; } = 500f;

        public virtual float MinJD { get; set; } = 10f;
        public virtual float MaxJD { get; set; } = 35f;
        public virtual float MinRT { get; set; } = 200f;
        public virtual float MaxRT { get; set; } = 1500f;

        /// <summary>スライダーの最小単位</summary>
        public virtual float JDStep { get; set; } = 0.05f;
        public virtual float RTStep { get; set; } = 5f;

        /// <summary>MODS タブの並びで JDMemory を先頭にする（次回起動から反映）</summary>
        public virtual bool PinTabFirst { get; set; } = true;

        /// <summary>起動時に GitHub で新しい版を確認する</summary>
        public virtual bool CheckUpdates { get; set; } = true;

        /// <summary>新しい版を自動で取得・照合して IPA\Pending に置く（次の起動で入れ替わる）。オフなら知らせるだけ</summary>
        public virtual bool AutoUpdate { get; set; } = true;

        /// <summary>画面に表示する履歴の件数</summary>
        public virtual int HistoryLines { get; set; } = 5;
    }
}
