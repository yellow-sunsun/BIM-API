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

        public string Level { get; set; }          // 錯誤所在樓層
        public long StdElementId { get; set; }     // 標準模型中的元件 Id（0 = 沒有，例如多餘構件）
        public long StuElementId { get; set; }     // 學生模型中的元件 Id（0 = 沒有，例如缺少構件）
        // 學生模型的原始座標（未經偏移校正），在學生模型裡定位錯誤用
        public double RawStudentX { get; set; }
        public double RawStudentY { get; set; }
        public double RawStudentZ { get; set; }
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

            // 樓層篩選
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
                        Level = stdEl.Level,
                        StdElementId = stdEl.Id,
                        RawStudentX = stdEl.X - offset.X,
                        RawStudentY = stdEl.Y - offset.Y,
                        RawStudentZ = stdEl.Z - offset.Z,
                        Detail = $"\u9019\u88e1\u61c9\u8a72\u6709\u4e00\u500b\u3010{stdEl.Category}\u3011({stdEl.FamilyType})\uff0c\u4f46\u5b78\u751f\u6a21\u578b\u4e2d\u627e\u4e0d\u5230"
                    });
                }
                else
                {
                    matched.Add(foundIdx);
                    var stuEl = correctedStudent.Elements[foundIdx];
                    var rawEl = filteredStu.Elements[foundIdx];

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
                            Level = stdEl.Level,
                            StdElementId = stdEl.Id,
                            StuElementId = rawEl.Id,
                            RawStudentX = rawEl.X,
                            RawStudentY = rawEl.Y,
                            RawStudentZ = rawEl.Z,
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
                            Level = stdEl.Level,
                            StdElementId = stdEl.Id,
                            StuElementId = rawEl.Id,
                            RawStudentX = rawEl.X,
                            RawStudentY = rawEl.Y,
                            RawStudentZ = rawEl.Z,
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
                    var rawEl = filteredStu.Elements[j];
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
                        Level = rawEl.Level,
                        StuElementId = rawEl.Id,
                        RawStudentX = rawEl.X,
                        RawStudentY = rawEl.Y,
                        RawStudentZ = rawEl.Z,
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
                            Level = missing.Level,
                            StdElementId = missing.StdElementId,
                            StuElementId = extra.StuElementId,
                            RawStudentX = extra.RawStudentX,
                            RawStudentY = extra.RawStudentY,
                            RawStudentZ = extra.RawStudentZ,

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
            // 配對投票：拿同類別的每一對構件（標準 × 學生）各算一個候選偏移，
            // 選「套用後能對上最多構件」的那個。不受建模順序影響，
            // 學生少幾根或多幾根構件也不會把偏移拉走（平均座標會被拉偏）。
            // 優先用結構柱投票；沒有柱的樓層（例如屋頂層）改用梁、牆、樓板
            var priority = new List<string> { "\u7d50\u69cb\u67f1", "\u7d50\u69cb\u69cb\u67b6", "\u7246", "\u6a13\u677f" };
            foreach (var el in standard.Elements)
                if (!priority.Contains(el.Category)) priority.Add(el.Category);

            foreach (var cat in priority)
            {
                var stdEls = standard.Elements.FindAll(e => e.Category == cat);
                var stuEls = student.Elements.FindAll(e => e.Category == cat);
                if (stdEls.Count == 0 || stuEls.Count == 0) continue;
                return VoteOffset(stdEls, stuEls, GetTolerance(cat));
            }
            return new XYZOffset { X = 0, Y = 0, Z = 0 };
        }

        private XYZOffset VoteOffset(List<ElementSnapshot> std, List<ElementSnapshot> stu, double tolerance)
        {
            XYZOffset best = null;
            int bestScore = -1;
            double bestResidual = double.MaxValue;

            // 第一步（快速篩選）：每一對構件的偏移量丟進邊長 = 容許值的格子裡投票，
            // 正確的偏移會讓大部分構件對落在同一格。只挑票數最高的幾格當候選，
            // 不必對每一對都做完整比對（構件上百個時，原本的做法要算好幾分鐘）
            var bins = new Dictionary<(long X, long Y, long Z), Bin>();
            foreach (var s in std)
            {
                foreach (var t in stu)
                {
                    double ox = s.X - t.X, oy = s.Y - t.Y, oz = s.Z - t.Z;
                    var key = (Cell(ox, tolerance), Cell(oy, tolerance), Cell(oz, tolerance));
                    Bin b;
                    if (!bins.TryGetValue(key, out b)) { b = new Bin(); bins[key] = b; }
                    b.Count++; b.SumX += ox; b.SumY += oy; b.SumZ += oz;
                }
            }

            // 每格的票數連同周圍 26 格一起算（避免正確偏移剛好落在格線上被拆票），
            // 候選偏移取這一區所有構件對的平均
            var ranked = new List<((long X, long Y, long Z) Key, int Votes, XYZOffset Offset)>();
            foreach (var key in bins.Keys)
            {
                int votes = 0;
                double sx0 = 0, sy0 = 0, sz0 = 0;
                for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                        for (long dz = -1; dz <= 1; dz++)
                        {
                            Bin nb;
                            if (!bins.TryGetValue((key.X + dx, key.Y + dy, key.Z + dz), out nb)) continue;
                            votes += nb.Count; sx0 += nb.SumX; sy0 += nb.SumY; sz0 += nb.SumZ;
                        }
                ranked.Add((key, votes, new XYZOffset { X = sx0 / votes, Y = sy0 / votes, Z = sz0 / votes }));
            }
            // 票數高的優先；同票時依格子座標排序，確保結果與構件順序無關
            ranked.Sort((a, b) =>
            {
                if (a.Votes != b.Votes) return b.Votes.CompareTo(a.Votes);
                if (a.Key.X != b.Key.X) return a.Key.X.CompareTo(b.Key.X);
                if (a.Key.Y != b.Key.Y) return a.Key.Y.CompareTo(b.Key.Y);
                return a.Key.Z.CompareTo(b.Key.Z);
            });

            // 第二步：只對前幾名候選做完整比對，選能對上最多構件的
            for (int k = 0; k < Math.Min(VOTE_CANDIDATES, ranked.Count); k++)
            {
                {
                    var cand = ranked[k].Offset;
                    int score;
                    double residual;
                    ScoreOffset(std, stu, cand, tolerance, out score, out residual);

                    // 得票相同時取殘差較小者，再相同時取偏移量較小者，確保結果與順序無關
                    bool better = score > bestScore
                        || (score == bestScore && residual < bestResidual - 1e-6)
                        || (score == bestScore && Math.Abs(residual - bestResidual) <= 1e-6 && Length(cand) < Length(best));
                    if (better)
                    {
                        best = cand;
                        bestScore = score;
                        bestResidual = residual;
                    }
                }
            }

            // 微調：用對上的構件的平均差修正偏移，讓結果更精準
            double sx = 0, sy = 0, sz = 0;
            int n = 0;
            foreach (var s in std)
            {
                var t = Nearest(s, stu, best, tolerance);
                if (t == null) continue;
                sx += s.X - t.X; sy += s.Y - t.Y; sz += s.Z - t.Z; n++;
            }
            if (n > 0) return new XYZOffset { X = sx / n, Y = sy / n, Z = sz / n };
            return best;
        }

        private const int VOTE_CANDIDATES = 10;

        private class Bin
        {
            public int Count;
            public double SumX, SumY, SumZ;
        }

        private static long Cell(double v, double size)
        {
            return (long)Math.Floor(v / size);
        }

        // 套用偏移後，有幾個標準構件在容許範圍內找得到學生構件，以及其距離總和
        private void ScoreOffset(List<ElementSnapshot> std, List<ElementSnapshot> stu, XYZOffset d,
            double tolerance, out int score, out double residual)
        {
            score = 0;
            residual = 0;
            foreach (var s in std)
            {
                var t = Nearest(s, stu, d, tolerance);
                if (t == null) continue;
                score++;
                residual += Dist(s, t, d);
            }
        }

        private ElementSnapshot Nearest(ElementSnapshot s, List<ElementSnapshot> stu, XYZOffset d, double tolerance)
        {
            ElementSnapshot best = null;
            double bestDist = double.MaxValue;
            foreach (var t in stu)
            {
                double xy = Math.Sqrt(Math.Pow(t.X + d.X - s.X, 2) + Math.Pow(t.Y + d.Y - s.Y, 2));
                double z = Math.Abs(t.Z + d.Z - s.Z);
                if (xy > tolerance || z > tolerance) continue;
                double dist = Math.Sqrt(xy * xy + z * z);
                if (dist < bestDist) { bestDist = dist; best = t; }
            }
            return best;
        }

        private double Dist(ElementSnapshot s, ElementSnapshot t, XYZOffset d)
        {
            return Math.Sqrt(Math.Pow(t.X + d.X - s.X, 2) + Math.Pow(t.Y + d.Y - s.Y, 2) + Math.Pow(t.Z + d.Z - s.Z, 2));
        }

        private double Length(XYZOffset d)
        {
            if (d == null) return double.MaxValue;
            return Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
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
                    Rotation = el.Rotation,
                    Level = el.Level
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