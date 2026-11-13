using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IPA.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace JDMemory.Core
{
    /// <summary>1回分のプレイ記録</summary>
    internal sealed class HistoryEntry
    {
        public DateTime Time { get; set; }
        public float JD { get; set; }
        public float RT { get; set; }
        public float NJS { get; set; }
        public float OriginalJD { get; set; }
        public float Speed { get; set; } = 1f;
        public bool Practice { get; set; }

        /// <summary>JDMemory が JD を変更したか（オフ時は false）</summary>
        public bool Applied { get; set; }

        /// <summary>Cleared / Failed / Quit / Restart</summary>
        public string Result { get; set; } = "";

        /// <summary>精度（%）。モディファイア倍率を含まない値</summary>
        public float? Accuracy { get; set; }

        /// <summary>ミス＋バッドカットの数</summary>
        public int Misses { get; set; }

        public float EndTime { get; set; }
        public float SongDuration { get; set; }
    }

    /// <summary>1譜面（曲＋モード＋難易度）分の記録</summary>
    internal sealed class MapHistory
    {
        public string SongName { get; set; } = "";
        public string SongSubName { get; set; } = "";
        public string SongAuthor { get; set; } = "";
        public string Mappers { get; set; } = "";
        public string Characteristic { get; set; } = "";
        public string Difficulty { get; set; } = "";

        /// <summary>この譜面で最後に遊んだ JD</summary>
        public float? LastJD { get; set; }

        public List<HistoryEntry> Entries { get; set; } = new List<HistoryEntry>();

        public HistoryEntry Best()
        {
            // クリアした中で精度が一番高いもの（練習以外を優先）
            var cleared = Entries.Where(e => e.Result == "Cleared" && e.Accuracy.HasValue).ToList();
            var normal = cleared.Where(e => !e.Practice).ToList();
            var pool = normal.Count > 0 ? normal : cleared;
            return pool.OrderByDescending(e => e.Accuracy.Value).FirstOrDefault();
        }
    }

    internal sealed class HistoryData
    {
        public int Version { get; set; } = 1;

        /// <summary>最後に遊んだ JD / RT（どの譜面でも）</summary>
        public float? LastJD { get; set; }
        public float? LastRT { get; set; }

        public Dictionary<string, MapHistory> Maps { get; set; } = new Dictionary<string, MapHistory>();
    }

    /// <summary>
    /// 履歴の読み書き。UserData/JDMemory/history.json に保存する。
    /// </summary>
    internal sealed class HistoryStore
    {
        private const int MaxEntriesPerMap = 200;

        internal static HistoryStore Instance { get; set; }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            // 譜面IDを辞書のキーにしているので、キーは変換しない（プロパティ名だけ camelCase）
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
            },
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Local,
        };

        private readonly string _path;
        private HistoryData _data;

        private HistoryStore(string path, HistoryData data)
        {
            _path = path;
            _data = data ?? new HistoryData();
        }

        internal static HistoryStore Load()
        {
            string dir = Path.Combine(UnityGame.UserDataPath, "JDMemory");
            string path = Path.Combine(dir, "history.json");
            HistoryData data = null;
            try
            {
                Directory.CreateDirectory(dir);
                if (File.Exists(path))
                {
                    data = JsonConvert.DeserializeObject<HistoryData>(File.ReadAllText(path), JsonSettings);
                }
            }
            catch (Exception e)
            {
                // 壊れたファイルは上書きせず、別名で残してから新しく始める
                Plugin.Log.Error($"history.json を読めませんでした: {e.Message}");
                try
                {
                    string broken = Path.Combine(dir, $"history.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    File.Move(path, broken);
                    Plugin.Log.Warn($"読めなかったファイルを {broken} に退避しました");
                }
                catch (Exception moveError)
                {
                    Plugin.Log.Error(moveError);
                }
                data = null;
            }

            var store = new HistoryStore(path, data);
            Plugin.Log.Info($"履歴を読み込みました: {store._data.Maps.Count} 譜面");
            return store;
        }

        internal float? LastJD => _data.LastJD;
        internal float? LastRT => _data.LastRT;

        internal MapHistory Get(string keyString)
        {
            return keyString != null && _data.Maps.TryGetValue(keyString, out var history) ? history : null;
        }

        internal void Add(MapSelection selection, HistoryEntry entry)
        {
            if (!_data.Maps.TryGetValue(selection.KeyString, out var history))
            {
                history = new MapHistory();
                _data.Maps[selection.KeyString] = history;
            }

            // 曲名などは最新の情報で上書き（JSONを人が見ても分かるように）
            history.SongName = selection.SongName;
            history.SongSubName = selection.SongSubName;
            history.SongAuthor = selection.SongAuthor;
            history.Mappers = selection.Mappers;
            history.Characteristic = selection.Characteristic;
            history.Difficulty = selection.Difficulty;

            history.Entries.Add(entry);
            if (history.Entries.Count > MaxEntriesPerMap)
            {
                history.Entries.RemoveRange(0, history.Entries.Count - MaxEntriesPerMap);
            }
            history.LastJD = entry.JD;

            _data.LastJD = entry.JD;
            _data.LastRT = entry.RT;

            Save();
        }

        internal void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_data, JsonSettings);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(tmp, _path, _path + ".bak");
                    }
                    catch (Exception)
                    {
                        File.Copy(_path, _path + ".bak", true);
                        File.Copy(tmp, _path, true);
                        File.Delete(tmp);
                    }
                }
                else
                {
                    File.Move(tmp, _path);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"history.json を保存できませんでした: {e}");
            }
        }
    }
}
