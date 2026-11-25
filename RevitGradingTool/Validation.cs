using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RevitGradingTool
{
    // 測試模型的自動驗證：匯入學生模型時，如果學生資料夾裡有 pyRevit 產生的「錯誤模型答案表.csv」，
    // 就把每個學生「答案表預期的錯誤數」和「比對出來的錯誤數」對照，並把所有快照存成 JSON，
    // 方便離線重跑比對、找出判定錯誤的原因。
    // 正式批改（資料夾裡沒有答案表）時什麼都不做、不會產生任何檔案。
    public static class TestValidation
    {
        public const string FOLDER = "驗證資料";
        public const string REPORT = "驗證報告.csv";

        // 回傳給面板顯示的摘要；不是測試模型時回傳 null
        public static string Run(string standardRvtPath, ModelSnapshot standard, List<StudentResult> students)
        {
            if (standard == null || students == null || students.Count == 0) return null;
            string dir = Path.GetDirectoryName(students[0].RvtPath);
            string table = FindAnswerTable(dir, standardRvtPath);
            if (table == null) return null;

            var expected = ReadExpected(table);
            string outDir = Path.Combine(dir, FOLDER);
            Directory.CreateDirectory(outDir);

            SaveSnapshot(outDir, Path.GetFileNameWithoutExtension(standardRvtPath), standard);

            var engine = new ComparisonEngine();
            var lines = new List<string> { "學生,預期錯誤數,實際錯誤數,結果,答案表說明,實際錯誤" };
            int same = 0, diff = 0, unknown = 0;

            foreach (var s in students)
            {
                if (s.Snapshot == null) continue;
                SaveSnapshot(outDir, s.Name, s.Snapshot);

                var errors = engine.Compare(standard, s.Snapshot);
                expected.TryGetValue(s.Name, out var exp);
                string result;
                if (exp == null || exp.Count < 0) { result = "答案表沒有預期錯誤數"; unknown++; }
                else if (exp.Count == errors.Count) { result = "一致"; same++; }
                else { result = "不一致"; diff++; }

                string actual = string.Join(" / ", errors.Select(e =>
                    $"[{TypeName(e.Type)}] {e.Category} {e.FamilyType} {e.Level}：{e.Detail}"));
                lines.Add(string.Join(",", new[]
                {
                    Cell(s.Name), exp == null || exp.Count < 0 ? "" : exp.Count.ToString(), errors.Count.ToString(),
                    Cell(result), Cell(exp?.Description ?? ""), Cell(actual)
                }));
            }

            File.WriteAllText(Path.Combine(outDir, REPORT), string.Join("\r\n", lines) + "\r\n",
                new UTF8Encoding(true));

            string msg = $"自動驗證：{same} 位學生的錯誤數與答案表一致";
            if (diff > 0) msg += $"、{diff} 位不一致";
            if (unknown > 0) msg += $"、{unknown} 位答案表沒有預期數（請用新版腳本重新產生）";
            return msg + $"（報告：{FOLDER}\\{REPORT}）";
        }

        private class Expected
        {
            public int Count = 0;          // -1 = 答案表沒有「預期錯誤數」欄（舊版答案表）
            public string Description = "";
        }

        private static string FindAnswerTable(string dir, string standardRvtPath)
        {
            string exact = Path.Combine(dir, Path.GetFileNameWithoutExtension(standardRvtPath) + " 錯誤模型答案表.csv");
            if (File.Exists(exact)) return exact;
            var any = Directory.GetFiles(dir, "*錯誤模型答案表.csv");
            return any.Length > 0 ? any[0] : null;
        }

        // 答案表：同一個檔名可能有好幾列（綜合題），「預期錯誤數」加總
        private static Dictionary<string, Expected> ReadExpected(string path)
        {
            var result = new Dictionary<string, Expected>();
            var rows = ParseCsv(File.ReadAllText(path, Encoding.UTF8).Replace("﻿", ""));
            if (rows.Count == 0) return result;
            int nCol = rows[0].IndexOf("預期錯誤數");
            int descCol = rows[0].IndexOf("錯誤說明");

            foreach (var r in rows.Skip(1))
            {
                if (r.Count == 0 || string.IsNullOrWhiteSpace(r[0])) continue;
                string name = Path.GetFileNameWithoutExtension(r[0].Trim());
                if (!result.TryGetValue(name, out var e)) { e = new Expected(); result[name] = e; }
                if (nCol >= 0 && nCol < r.Count && int.TryParse(r[nCol], out int n))
                {
                    if (e.Count >= 0) e.Count += n;
                }
                else e.Count = -1;
                if (descCol >= 0 && descCol < r.Count && e.Description == "") e.Description = r[descCol];
            }
            return result;
        }

        // 簡單的 CSV 解析（支援引號、引號內的逗號與換行）
        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); }
                else if (ch != '\r') cell.Append(ch);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }

        private static void SaveSnapshot(string dir, string name, ModelSnapshot snap)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            File.WriteAllText(Path.Combine(dir, name + "_snapshot.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(snap, Newtonsoft.Json.Formatting.Indented));
        }

        private static string Cell(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private static string TypeName(ErrorType t)
        {
            switch (t)
            {
                case ErrorType.Missing: return "缺少";
                case ErrorType.Extra: return "多餘";
                case ErrorType.WrongRotation: return "旋轉錯誤";
                case ErrorType.WrongPosition: return "位置偏移";
                default: return "屬性錯誤";
            }
        }
    }
}