using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RevitGradingTool
{
    // 用法：dotnet run --project tools/GradingValidator -- "<驗證資料資料夾>" ["<答案表.csv>"] [--all]
    //   驗證資料資料夾：批改面板匯入測試模型時自動產生（學生模型資料夾裡的「驗證資料」）
    //   答案表：預設是驗證資料資料夾上一層的「*錯誤模型答案表.csv」
    //   --all：一致的學生也列出細節
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 1) { Console.WriteLine("用法：GradingValidator <驗證資料資料夾> [答案表.csv] [--all]"); return 2; }
            string snapDir = args[0];
            string table = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--"))
                           ?? Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(snapDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "*錯誤模型答案表.csv").FirstOrDefault();
            if (table == null) { Console.WriteLine("找不到答案表"); return 2; }
            return Validate(snapDir, table, args.Contains("--all"));
        }

        static ModelSnapshot Load(string p) => System.Text.Json.JsonSerializer.Deserialize<ModelSnapshot>(File.ReadAllText(p));

        static int Validate(string snapDir, string answerTable, bool verbose)
        {
            var rows = ParseCsv(File.ReadAllText(answerTable, Encoding.UTF8).Replace("\uFEFF", ""));
            int nCol = rows[0].IndexOf("預期錯誤數"), xCol = rows[0].IndexOf("改動細節");
            var exp = new Dictionary<string, (int n, List<string> details)>();
            foreach (var r in rows.Skip(1).Where(r => r.Count > 1 && r[0] != ""))
            {
                string name = Path.GetFileNameWithoutExtension(r[0].Trim());
                exp.TryGetValue(name, out var e);
                if (e.details == null) e = (0, new List<string>());
                int n = nCol >= 0 && nCol < r.Count && int.TryParse(r[nCol], out int v) ? v : -1;
                e.n = (e.n < 0 || n < 0) ? -1 : e.n + n;
                if (xCol >= 0 && xCol < r.Count) e.details.Add(r[xCol]);
                exp[name] = e;
            }

            var files = Directory.GetFiles(snapDir, "*_snapshot.json");
            string stdFile = files.FirstOrDefault(f => !exp.ContainsKey(Path.GetFileName(f).Replace("_snapshot.json", "")));
            if (stdFile == null) { Console.WriteLine("找不到標準模型的快照"); return 2; }
            var std = Load(stdFile);
            Console.WriteLine($"標準模型：{Path.GetFileName(stdFile)}（{std.Elements.Count} 個構件）；答案表：{Path.GetFileName(answerTable)}");

            int ok = 0, bad = 0;
            var engine = new ComparisonEngine();
            foreach (var f in files.Where(f => f != stdFile).OrderBy(f => f))
            {
                string name = Path.GetFileName(f).Replace("_snapshot.json", "");
                var errs = engine.Compare(std, Load(f));
                exp.TryGetValue(name, out var e);
                bool same = e.details != null && e.n == errs.Count;
                if (same) ok++; else bad++;
                if (same && !verbose) continue;
                Console.WriteLine($"\n{(same ? "一致" : "不一致")}  {name}：預期 {(e.details == null || e.n < 0 ? "?" : e.n.ToString())}，實際 {errs.Count}");
                if (e.details != null) foreach (var d in e.details) Console.WriteLine($"    答案表：{d}");
                foreach (var x in errs)
                    Console.WriteLine($"    實際：[{x.Type}] {x.Category}[{x.CategoryKey}] {x.FamilyName}/{x.FamilyType} 樓層={x.Level} " +
                                      $"std={x.StdElementId} stu={x.StuElementId} 位置=({x.X:F0},{x.Y:F0},{x.Z:F0})：{x.Detail}");
            }
            Console.WriteLine($"\n合計：一致 {ok}，不一致 {bad}");
            return bad == 0 ? 0 : 1;
        }

        static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder(); bool q = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (q) { if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else q = false; } else cell.Append(ch); }
                else if (ch == '"') q = true;
                else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); }
                else if (ch != '\r') cell.Append(ch);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
