using System;
using System.Collections.Generic;

namespace RevitGradingTool
{
    public enum ErrorType
    {
        Missing,
        Extra,
        WrongAttr
    }

    public class CompareError
    {
        public ErrorType Type { get; set; }
        public string Category { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string Detail { get; set; }
    }

    public class ComparisonEngine
    {
        private const double TOLERANCE_COLUMN = 50.0;
        private const double TOLERANCE_BEAM = 300.0;
        private const double TOLERANCE_FLOOR = 500.0;
        private const double TOLERANCE_WALL = 500.0;

        private class XYZOffset
        {
            public double X, Y, Z;
        }

        public List<CompareError> Compare(ModelSnapshot standard, ModelSnapshot student)
        {
            var errors = new List<CompareError>();
            var matched = new HashSet<int>();

            // 計算座標偏移，校正學生模型整體偏移
            XYZOffset offset = CalcOffset(standard, student);
            ModelSnapshot correctedStudent = ApplyOffset(student, offset);

            for (int i = 0; i < standard.Elements.Count; i++)
            {
                var stdEl = standard.Elements[i];
                int foundIdx = FindClosest(stdEl, correctedStudent.Elements, matched);

                if (foundIdx == -1)
                {
                    errors.Add(new CompareError
                    {
                        Type = ErrorType.Missing,
                        Category = stdEl.Category,
                        X = stdEl.X,
                        Y = stdEl.Y,
                        Z = stdEl.Z,
                        Detail = $"\u9019\u88e1\u61c9\u8a72\u6709\u4e00\u500b\u3010{stdEl.Category}\u3011({stdEl.FamilyType})\uff0c\u4f46\u5b78\u751f\u6a21\u578b\u4e2d\u627e\u4e0d\u5230"
                    });
                }
                else
                {
                    matched.Add(foundIdx);
                    var stuEl = correctedStudent.Elements[foundIdx];
                    var attrErrors = CheckAttributes(stdEl, stuEl);
                    if (attrErrors.Count > 0)
                    {
                        errors.Add(new CompareError
                        {
                            Type = ErrorType.WrongAttr,
                            Category = stdEl.Category,
                            X = stuEl.X,
                            Y = stuEl.Y,
                            Z = stuEl.Z,
                            Detail = string.Join("\uff1b", attrErrors)
                        });
                    }
                }
            }

            for (int j = 0; j < correctedStudent.Elements.Count; j++)
            {
                if (!matched.Contains(j))
                {
                    var stuEl = correctedStudent.Elements[j];
                    errors.Add(new CompareError
                    {
                        Type = ErrorType.Extra,
                        Category = stuEl.Category,
                        X = stuEl.X,
                        Y = stuEl.Y,
                        Z = stuEl.Z,
                        Detail = $"\u9019\u500b\u3010{stuEl.Category}\u3011({stuEl.FamilyType})\u662f\u591a\u9918\u7684\uff0c\u6a19\u6e96\u6a21\u578b\u6c92\u6709"
                    });
                }
            }

            return errors;
        }

        private XYZOffset CalcOffset(ModelSnapshot standard, ModelSnapshot student)
        {
            // 找第一對同類別的柱來估算偏移，柱的座標最穩定
            foreach (var stdEl in standard.Elements)
            {
                if (stdEl.Category != "\u7d50\u69cb\u67f1") continue;
                foreach (var stuEl in student.Elements)
                {
                    if (stuEl.Category != "\u7d50\u69cb\u67f1") continue;
                    return new XYZOffset
                    {
                        X = stdEl.X - stuEl.X,
                        Y = stdEl.Y - stuEl.Y,
                        Z = stdEl.Z - stuEl.Z
                    };
                }
            }
            // 沒有柱的話用任意第一對同類別構件
            foreach (var stdEl in standard.Elements)
            {
                foreach (var stuEl in student.Elements)
                {
                    if (stdEl.Category != stuEl.Category) continue;
                    return new XYZOffset
                    {
                        X = stdEl.X - stuEl.X,
                        Y = stdEl.Y - stuEl.Y,
                        Z = stdEl.Z - stuEl.Z
                    };
                }
            }
            return new XYZOffset { X = 0, Y = 0, Z = 0 };
        }

        private ModelSnapshot ApplyOffset(ModelSnapshot student, XYZOffset offset)
        {
            var corrected = new ModelSnapshot { ModelName = student.ModelName };
            foreach (var el in student.Elements)
            {
                corrected.Elements.Add(new ElementSnapshot
                {
                    Id = el.Id,
                    Category = el.Category,
                    X = el.X + offset.X,
                    Y = el.Y + offset.Y,
                    Z = el.Z + offset.Z,
                    FamilyType = el.FamilyType,
                    Material = el.Material,
                    Width = el.Width,
                    Height = el.Height,
                    Depth = el.Depth
                });
            }
            return corrected;
        }

        private double GetTolerance(string category)
        {
            if (category == "\u7d50\u69cb\u69cb\u67b6") return TOLERANCE_BEAM;
            if (category == "\u6a13\u677f") return TOLERANCE_FLOOR;
            if (category == "\u7246") return TOLERANCE_WALL;
            return TOLERANCE_COLUMN;
        }

        private int FindClosest(ElementSnapshot target, List<ElementSnapshot> candidates, HashSet<int> used)
        {
            int bestIdx = -1;
            double bestDist = double.MaxValue;
            double tolerance = GetTolerance(target.Category);

            for (int i = 0; i < candidates.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (candidates[i].Category != target.Category) continue;

                var c = candidates[i];

                // XY 和 Z 分開判斷
                double xyDist = Math.Sqrt(Math.Pow(c.X - target.X, 2) + Math.Pow(c.Y - target.Y, 2));
                double zDist = Math.Abs(c.Z - target.Z);

                if (xyDist > tolerance) continue;
                if (zDist > tolerance) continue;

                double dist = Math.Sqrt(xyDist * xyDist + zDist * zDist);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        private double Distance(double x1, double y1, double z1,
                                double x2, double y2, double z2)
        {
            return Math.Sqrt(Math.Pow(x2 - x1, 2) +
                             Math.Pow(y2 - y1, 2) +
                             Math.Pow(z2 - z1, 2));
        }

        private List<string> CheckAttributes(ElementSnapshot std, ElementSnapshot stu)
        {
            var errors = new List<string>();

            if (!string.IsNullOrEmpty(std.FamilyType) && std.FamilyType != stu.FamilyType)
                errors.Add($"\u65cf\u7fa4\u985e\u578b\u61c9\u70ba\u300c{std.FamilyType}\u300d\uff0c\u5b78\u751f\u586b\u7684\u662f\u300c{stu.FamilyType}\u300d");

            if (!string.IsNullOrEmpty(std.Material) && std.Material != stu.Material)
                errors.Add($"\u6750\u8cea\u61c9\u70ba\u300c{std.Material}\u300d\uff0c\u5b78\u751f\u586b\u7684\u662f\u300c{stu.Material}\u300d");

            return errors;
        }
    }
}