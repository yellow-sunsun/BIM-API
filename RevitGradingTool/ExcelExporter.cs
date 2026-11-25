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
            lines.Add("\u5b78\u751f,\u932f\u8aa4\u7e3d\u6578,\u7f3a\u5c11\u69cb\u4ef6,\u591a\u9918\u69cb\u4ef6,\u5c6c\u6027\u932f\u8aa4");

            foreach (var s in students)
            {
                int missing = s.Errors.FindAll(e => e.Type == ErrorType.Missing).Count;
                int extra = s.Errors.FindAll(e => e.Type == ErrorType.Extra).Count;
                int wrongAttr = s.Errors.FindAll(e => e.Type == ErrorType.WrongAttr).Count;

                lines.Add($"{s.Name},{s.Errors.Count},{missing},{extra},{wrongAttr}");
            }

            lines.Add("");
            lines.Add("--- \u932f\u8aa4\u8a73\u7d30 ---");
            lines.Add("\u5b78\u751f,\u932f\u8aa4\u985e\u578b,\u5206\u7d44,\u69cb\u4ef6\u985e\u5225,\u6a13\u5c64,\u8aaa\u660e,X(mm),Y(mm),Z(mm)");

            foreach (var s in students)
            {
                foreach (var err in s.Errors)
                {
                    string typeName = err.Type == ErrorType.Missing ? "\u7f3a\u5c11\u69cb\u4ef6" :
                                      err.Type == ErrorType.Extra ? "\u591a\u9918\u69cb\u4ef6" :
                                      err.Type == ErrorType.WrongRotation ? "\u65cb\u8f49\u932f\u8aa4" :
                                      err.Type == ErrorType.WrongPosition ? "\u4f4d\u7f6e\u504f\u79fb" : "\u5c6c\u6027\u932f\u8aa4";
                    string detail = err.Detail.Replace(",", "\uff0c");
                    lines.Add($"{s.Name},{typeName},{err.Group},{err.Category},{err.Level},{detail},{err.X:F0},{err.Y:F0},{err.Z:F0}");
                }
            }

            File.WriteAllLines(csvPath, lines, System.Text.Encoding.UTF8);
        }
    }
}