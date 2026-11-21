using System;
using System.Collections.Generic;

namespace RevitGradingTool
{
    public enum ErrorType
    {
        Missing,
        Extra,
        WrongAttr,
        WrongRotation,
        WrongPosition
    }

    public class CompareError
    {
        public ErrorType Type { get; set; }
        public string Category { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double StudentX { get; set; }
        public double StudentY { get; set; }
        public double StudentZ { get; set; }
        public string Detail { get; set; }
    }

    public class ComparisonEngine
    {
        private const double TOLERANCE_COLUMN = 50.0;
        private const double TOLERANCE_BEAM = 100.0;
        private const double TOLERANCE_FLOOR = 500.0;
        private const double TOLERANCE_WALL = 500.0;

        private class XYZOffset
        {
            public double X, Y, Z;
        }

        public List<CompareError> Compare(ModelSnapshot standard, ModelSnapshot student,
            string selectedFloor = "\u5168\u90e8\u6a13\u5c64",
            Dictionary<string, (double MinZ, double MaxZ)> floorRanges = null)
        {
            var errors = new List<CompareError>();
            var matched = new HashSet<int>();

            // ¼Ó¼h¿z¿ï
            var stdElements = FilterByFloor(standard.Elements, selectedFloor, floorRanges);
            var stuElements = FilterByFloor(student.Elements, selectedFloor, floorRanges);

            var filteredStd = new ModelSnapshot { ModelName = standard.ModelName, Elements = stdElements };
            var filteredStu = new ModelSnapshot { ModelName = student.ModelName, Elements = stuElements };

            XYZOffset offset = CalcOffset(filteredStd, filteredStu);
            ModelSnapshot correctedStudent = ApplyOffset(filteredStu, offset);

            for (int i = 0; i < filteredStd.Elements.Count; i++)
            {
                var stdEl = filteredStd.Elements[i];
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
                        StudentX = stdEl.X,
                        StudentY = stdEl.Y,
                        StudentZ = stdEl.Z,
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
                            X = stdEl.X,
                            Y = stdEl.Y,
                            Z = stdEl.Z,
                            StudentX = stuEl.X,
                            StudentY = stuEl.Y,
                            StudentZ = stuEl.Z,
                            Detail = string.Join("\uff1b", attrErrors)
                        });
                    }

                    double rotDiff = Math.Abs(stdEl.Rotation - stuEl.Rotation);
                    if (rotDiff > 180) rotDiff = 360 - rotDiff;
                    if (rotDiff > 1.0)
                    {
                        errors.Add(new CompareError
                        {
                            Type = ErrorType.WrongRotation,
                            Category = stdEl.Category,
                            X = stdEl.X,
                            Y = stdEl.Y,
                            Z = stdEl.Z,
                            StudentX = stuEl.X,
                            StudentY = stuEl.Y,
                            StudentZ = stuEl.Z,
                            Detail = $"\u65cb\u8f49\u89d2\u5ea6\u61c9\u70ba {stdEl.Rotation:F1}\u00b0\uff0c\u5b78\u751f\u7684\u662f {stuEl.Rotation:F1}\u00b0"
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
                        StudentX = stuEl.X,
                        StudentY = stuEl.Y,
                        StudentZ = stuEl.Z,
                        Detail = $"\u9019\u500b\u3010{stuEl.Category}\u3011({stuEl.FamilyType})\u662f\u591a\u9918\u7684\uff0c\u6a19\u6e96\u6a21\u578b\u6c92\u6709"
                    });
                }
            }

            var missingErrors = errors.FindAll(e => e.Type == ErrorType.Missing);
            var extraErrors = errors.FindAll(e => e.Type == ErrorType.Extra);
            var toRemove = new List<CompareError>();
            var toAdd = new List<CompareError>();

            foreach (var missing in missingErrors)
            {
                foreach (var extra in extraErrors)
                {
                    if (extra.Category != missing.Category) continue;
                    if (toRemove.Contains(extra)) continue;

                    double dist = Math.Sqrt(
                        Math.Pow(extra.X - missing.X, 2) +
                        Math.Pow(extra.Y - missing.Y, 2) +
                        Math.Pow(extra.Z - missing.Z, 2));

                    if (dist < 1000.0)
                    {
                        toRemove.Add(missing);
                        toRemove.Add(extra);
                        toAdd.Add(new CompareError
                        {
                            Type = ErrorType.WrongPosition,
                            Category = missing.Category,
                            X = missing.X,
                            Y = missing.Y,
                            Z = missing.Z,
                            StudentX = extra.X,
                            StudentY = extra.Y,
                            StudentZ = extra.Z,
                            Detail = $"\u3010{missing.Category}\u3011\u4f4d\u7f6e\u504f\u79fb\uff0c\u61c9\u5728 X={missing.X:F0}, Y={missing.Y:F0}\uff0c\u5b78\u751f\u653e\u5728 X={extra.X:F0}, Y={extra.Y:F0}"
                        });
                        break;
                    }
                }
            }

            foreach (var r in toRemove) errors.Remove(r);
            errors.AddRange(toAdd);

            return errors;
        }

        private List<ElementSnapshot> FilterByFloor(List<ElementSnapshot> elements,
            string selectedFloor,
            Dictionary<string, (double MinZ, double MaxZ)> floorRanges)
        {
            if (selectedFloor == "\u5168\u90e8\u6a13\u5c64")
                return new List<ElementSnapshot>(elements);

            return elements.FindAll(el => el.Level == selectedFloor);
        }

        private XYZOffset CalcOffset(ModelSnapshot standard, ModelSnapshot student)
        {
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
                    Depth = el.Depth,
                    Rotation = el.Rotation
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