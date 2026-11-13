using System.Linq;

namespace JDMemory.Core
{
    /// <summary>
    /// 選択中の譜面（曲＋モード＋難易度）の情報。
    /// </summary>
    internal sealed class MapSelection
    {
        public BeatmapKey Key { get; private set; }
        public string KeyString { get; private set; }
        public BeatmapLevel Level { get; private set; }

        public string SongName { get; private set; } = "";
        public string SongSubName { get; private set; } = "";
        public string SongAuthor { get; private set; } = "";
        public string Mappers { get; private set; } = "";
        public string Characteristic { get; private set; } = "";
        public string Difficulty { get; private set; } = "";

        public float Bpm { get; private set; }
        public float Njs { get; private set; }
        public float Offset { get; private set; }

        /// <summary>譜面作者が設定したままの JD</summary>
        public float OriginalJD { get; private set; }

        /// <summary>この譜面で設定できる最小の JD</summary>
        public float MinJD { get; private set; }

        public float OriginalRT => JumpMath.ToReactionTime(OriginalJD, Njs);

        public static string MakeKeyString(BeatmapKey key)
        {
            string characteristic = key.beatmapCharacteristic != null ? key.beatmapCharacteristic.serializedName : "Unknown";
            return $"{key.levelId}|{characteristic}|{key.difficulty}";
        }

        public static MapSelection Create(BeatmapKey key, BeatmapLevel level)
        {
            if (level == null || !key.IsValid())
            {
                return null;
            }

            var selection = new MapSelection
            {
                Key = key,
                KeyString = MakeKeyString(key),
                Level = level,
                SongName = level.songName ?? "",
                SongSubName = level.songSubName ?? "",
                SongAuthor = level.songAuthorName ?? "",
                Characteristic = key.beatmapCharacteristic != null ? key.beatmapCharacteristic.serializedName : "Unknown",
                Difficulty = key.difficulty.ToString(),
                Bpm = level.beatsPerMinute,
                Njs = 10f,
                Offset = 0f,
            };

            BeatmapBasicData basicData = level.GetDifficultyBeatmapData(key.beatmapCharacteristic, key.difficulty);
            if (basicData != null)
            {
                if (basicData.noteJumpMovementSpeed > 0.01f)
                {
                    selection.Njs = basicData.noteJumpMovementSpeed;
                }
                selection.Offset = basicData.noteJumpStartBeatOffset;
                if (basicData.mappers != null && basicData.mappers.Length > 0)
                {
                    selection.Mappers = string.Join(", ", basicData.mappers);
                }
            }
            if (selection.Mappers.Length == 0 && level.allMappers != null)
            {
                selection.Mappers = string.Join(", ", level.allMappers.Where(m => !string.IsNullOrEmpty(m)));
            }

            selection.OriginalJD = JumpMath.JumpDistance(selection.Bpm, selection.Njs, selection.Offset);
            selection.MinJD = JumpMath.MinJumpDistance(selection.Bpm, selection.Njs);
            return selection;
        }

        /// <summary>画面表示用の難易度名</summary>
        public string DifficultyLabel
        {
            get
            {
                string diff = Difficulty == "ExpertPlus" ? "Expert+" : Difficulty;
                return Characteristic == "Standard" ? diff : $"{Characteristic} {diff}";
            }
        }
    }
}
