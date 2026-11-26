using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RevitGradingTool
{
    // 快照快取：學生 .rvt 轉好的快照存成 json，下次匯入時檔案沒變就直接讀 json，不用再開 Revit 檔。
    // 判斷「檔案沒變」：路徑、檔案大小、修改時間都相同，而且是同一版程式產生的
    // （每次重新編譯外掛，解析邏輯可能改了，舊快取就自動作廢）。
    public static class SnapshotCache
    {
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitGradingTool", "SnapshotCache");

        private static readonly string ParserVersion =
            typeof(SnapshotCache).Assembly.ManifestModule.ModuleVersionId.ToString();

        private class Entry
        {
            public string RvtPath { get; set; }
            public long Size { get; set; }
            public long LastWriteTicks { get; set; }
            public string ParserVersion { get; set; }
            public ModelSnapshot Snapshot { get; set; }
        }

        public static ModelSnapshot TryLoad(string rvtPath)
        {
            try
            {
                string cacheFile = CacheFileOf(rvtPath);
                if (!File.Exists(cacheFile)) return null;

                var entry = JsonConvert.DeserializeObject<Entry>(File.ReadAllText(cacheFile, Encoding.UTF8));
                var info = new FileInfo(rvtPath);
                if (entry?.Snapshot == null ||
                    entry.ParserVersion != ParserVersion ||
                    entry.Size != info.Length ||
                    entry.LastWriteTicks != info.LastWriteTimeUtc.Ticks)
                    return null;

                return entry.Snapshot;
            }
            catch
            {
                return null;   // 快取壞掉就當作沒有，重新開檔轉換
            }
        }

        public static void Save(string rvtPath, ModelSnapshot snapshot)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                var info = new FileInfo(rvtPath);
                var entry = new Entry
                {
                    RvtPath = Path.GetFullPath(rvtPath),
                    Size = info.Length,
                    LastWriteTicks = info.LastWriteTimeUtc.Ticks,
                    ParserVersion = ParserVersion,
                    Snapshot = snapshot
                };
                File.WriteAllText(CacheFileOf(rvtPath), JsonConvert.SerializeObject(entry), Encoding.UTF8);
            }
            catch
            {
                // 寫不進快取不影響評分，下次重新轉換即可
            }
        }

        private static string CacheFileOf(string rvtPath)
        {
            string key = Path.GetFullPath(rvtPath).ToLowerInvariant();
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var sb = new StringBuilder();
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return Path.Combine(CacheDir, sb + ".json");
            }
        }
    }
}
