using System;
using System.Collections.Generic;

namespace RevitGradingTool
{
    public enum ErrorType
    {
        Missing,   // 標準有，學生沒有
        Extra,     // 學生多建了
        WrongAttr  // 位置對但屬性錯
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
        private const double TOLERANCE = 50.0; // mm

        public List<CompareError> Compare(ModelSnapshot standard, ModelSnapshot student)
        {
            var errors = new List<CompareError>();
            var matched = new HashSet<int>();

            // 第一輪：標準模型每個構件，去學生模型找對應
            for (int i = 0; i < standard.Elements.Count; i++)
            {
                var stdEl = standard.Elements[i];
                int foundIdx = FindClosest(stdEl, student.Elements, matched);

                if (foundIdx == -1)
                {
                    errors.Add(new CompareError
                    {
                        Type = ErrorType.Missing,
                        Category = stdEl.Category,
                        X = stdEl.X,
                        Y = stdEl.Y,
                        Z = stdEl.Z,
                        Detail = $"這裡應該有一個【{stdEl.Category}】({stdEl.FamilyType})，但學生模型中找不到"
                    });
                }
                else
                {
                    matched.Add(foundIdx);
                    var stuEl = student.Elements[foundIdx];
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
                            Detail = string.Join("；", attrErrors)
                        });
                    }
                }
            }

            // 第二輪：學生模型中沒被匹配到的 → 多餘構件
            for (int j = 0; j < student.Elements.Count; j++)
            {
                if (!matched.Contains(j))
                {
                    var stuEl = student.Elements[j];
                    errors.Add(new CompareError
                    {
                        Type = ErrorType.Extra,
                        Category = stuEl.Category,
                        X = stuEl.X,
                        Y = stuEl.Y,
                        Z = stuEl.Z,
                        Detail = $"這個【{stuEl.Category}】({stuEl.FamilyType})是多餘的，標準模型沒有"
                    });
                }
            }

            return errors;
        }

        private int FindClosest(ElementSnapshot target, List<ElementSnapshot> candidates, HashSet<int> used)
        {
            int bestIdx = -1;
            double bestDist = double.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (candidates[i].Category != target.Category) continue;

                double dist = Distance(target.X, target.Y, target.Z,
                                       candidates[i].X, candidates[i].Y, candidates[i].Z);
                if (dist <= TOLERANCE && dist < bestDist)
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
                errors.Add($"族群類型應為「{std.FamilyType}」，學生填的是「{stu.FamilyType}」");

            if (!string.IsNullOrEmpty(std.Material) && std.Material != stu.Material)
                errors.Add($"材質應為「{std.Material}」，學生填的是「{stu.Material}」");

            return errors;
        }
    }
}