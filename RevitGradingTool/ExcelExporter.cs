using System.Collections.Generic;
using System.IO;

namespace RevitGradingTool
{
    public class ExcelExporter
    {
        public void Export(List<StudentResult> students, string outputPath)
        {
            // 用 CSV 格式輸出，Excel 可以直接開啟
            string csvPath = outputPath.Replace(".xlsx", ".csv");

            var lines = new List<string>();
            lines.Add("學生,錯誤總數,缺少構件,多餘構件,屬性錯誤");

            foreach (var s in students)
            {
                int missing = s.Errors.FindAll(e => e.Type == ErrorType.Missing).Count;
                int extra = s.Errors.FindAll(e => e.Type == ErrorType.Extra).Count;
                int wrongAttr = s.Errors.FindAll(e => e.Type == ErrorType.WrongAttr).Count;

                lines.Add($"{s.Name},{s.Errors.Count},{missing},{extra},{wrongAttr}");
            }

            lines.Add("");
            lines.Add("--- 錯誤詳細 ---");
            lines.Add("學生,錯誤類型,構件類別,說明,X(mm),Y(mm),Z(mm)");

            foreach (var s in students)
            {
                foreach (var err in s.Errors)
                {
                    string typeName = err.Type == ErrorType.Missing ? "缺少構件" :
                                      err.Type == ErrorType.Extra ? "多餘構件" : "屬性錯誤";
                    string detail = err.Detail.Replace(",", "，");
                    lines.Add($"{s.Name},{typeName},{err.Category},{detail},{err.X:F0},{err.Y:F0},{err.Z:F0}");
                }
            }

            File.WriteAllLines(csvPath, lines, System.Text.Encoding.UTF8);
        }
    }
}