using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitGradingTool
{
    [Transaction(TransactionMode.Manual)]
    public class ExportSnapshot : IExternalCommand
    {
        private const double FT_TO_MM = 304.8;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var snapshot = ParseDocument(doc);

            string json = JsonConvert.SerializeObject(snapshot, Newtonsoft.Json.Formatting.Indented);
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                doc.Title + "_snapshot.json");
            File.WriteAllText(path, json);

            TaskDialog.Show("\u532f\u51fa\u5b8c\u6210",
                $"\u5171\u532f\u51fa {snapshot.Elements.Count} \u500b\u69cb\u4ef6\n\u5b58\u6a94\u4f4d\u7f6e\uff1a{path}");

            return Result.Succeeded;
        }

        // 讀取模型中所有「實體構件」（排除註解、草圖線、材料、樓層、相機等）
        public static ModelSnapshot ParseDocument(Document doc)
        {
            var snapshot = new ModelSnapshot { ModelName = doc.Title };

            // 所有樓層依高度排序，找不到樓層參數時用高度判斷
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation).ToList();

            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (!ShouldCompare(el)) continue;
                snapshot.Elements.Add(ParseElement(doc, el, levels));
            }

            // 固定順序（類別、Id），每次匯出結果一致
            snapshot.Elements.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.CategoryKey, b.CategoryKey);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            return snapshot;
        }

        // 要比對的構件：學生「放了 / 沒放 / 放錯」都要能抓到的實體構件
        private static bool ShouldCompare(Element el)
        {
            var cat = el.Category;
            if (cat == null || cat.CategoryType != CategoryType.Model || el.ViewSpecific) return false;
            var bic = cat.BuiltInCategory;
            if (bic == BuiltInCategory.OST_Mass) return false;   // 量體是概念設計用，不是構件

            // 欄杆的組成零件（欄杆柱、頂部扶手、扶手、支撐、端點…）跟著欄杆比對，不單獨列出
            string bicName = bic.ToString();
            if (bicName.StartsWith("OST_Railing") || bic == BuiltInCategory.OST_StairsRailingBaluster ||
                bic == BuiltInCategory.OST_StairsRailingRail)
                return false;

            if (el is FamilyInstance fi)
            {
                if (fi.SuperComponent != null) return false;   // 巢狀子元件跟著上層族群比對，避免重複計算
                if (el is Panel || el is Mullion) return false; // 帷幕嵌板、豎框：以整面帷幕牆比對
                return true;
            }
            if (el is Wall w) return !w.IsStackedWallMember;   // 疊層牆的子牆跟著疊層牆
            if (el is HostObject) return true;                  // 樓板、屋頂、天花板、地形實體…
            if (el is Stairs || el is Railing) return true;     // 樓梯以整座比對（梯段、平台的資訊彙整在樓梯上）
            if (el is MEPCurve) return true;                    // 管、風管、電管、電纜架
            if (el is Opening) return true;                     // 豎井開口等
            if (el is DirectShape) return true;
            if (bic == BuiltInCategory.OST_Ramps) return true;
            return false;
        }

        private static ElementSnapshot ParseElement(Document doc, Element el, List<Level> levels)
        {
            var bic = el.Category.BuiltInCategory;
            var snap = new ElementSnapshot
            {
                Id = el.Id.Value,
                Category = el.Category?.Name ?? "Unknown",
                CategoryKey = bic != BuiltInCategory.INVALID ? bic.ToString() : (el.Category?.Name ?? ""),
                IsLinear = el.Location is LocationCurve
            };

            ReadPosition(el, bic, snap);
            snap.HostId = HostIdOf(el);

            // 讀取構件所在樓層（不同類別用不同參數）
            Parameter levelParam;
            if (bic == BuiltInCategory.OST_StructuralFraming)
                levelParam = el.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
            else if (bic == BuiltInCategory.OST_Floors)
                levelParam = el.get_Parameter(BuiltInParameter.LEVEL_PARAM);
            else if (el is Stairs)
                levelParam = el.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
            else
                levelParam = el.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                          ?? el.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);

            if (levelParam != null && levelParam.HasValue &&
                levelParam.StorageType == StorageType.ElementId)
            {
                var levelEl = doc.GetElement(levelParam.AsElementId());
                snap.Level = levelEl?.Name ?? "";
            }

            // 參數讀不到樓層（例如內建族群）：先看元素本身的 LevelId，再依高度找所在樓層
            if (string.IsNullOrEmpty(snap.Level))
            {
                Level lv = el.LevelId != ElementId.InvalidElementId ? doc.GetElement(el.LevelId) as Level : null;
                if (lv == null) lv = LevelAtHeight(levels, snap.Z / FT_TO_MM);
                snap.Level = lv?.Name ?? "";
            }

            var elType = doc.GetElement(el.GetTypeId()) as ElementType;
            snap.FamilyName = elType?.FamilyName ?? "";
            if (el is FamilyInstance fi)
                snap.FamilyType = fi.Symbol?.Name ?? "";
            else
                snap.FamilyType = el.Name;

            var matIds = el.GetMaterialIds(false);
            if (matIds.Count > 0)
            {
                Material mat = doc.GetElement(matIds.First()) as Material;
                snap.Material = mat?.Name ?? "";
            }

            snap.Width = GetParamMM(el, BuiltInParameter.GENERIC_WIDTH);
            snap.Height = GetParamMM(el, BuiltInParameter.GENERIC_HEIGHT);
            snap.Depth = GetParamMM(el, BuiltInParameter.GENERIC_DEPTH);

            ReadProperties(doc, el, bic, snap);
            return snap;
        }

        // 位置與旋轉角（原本 6 個類別的算法不變）
        private static void ReadPosition(Element el, BuiltInCategory bic, ElementSnapshot snap)
        {
            // 內建族群（in-place）的定位點固定在專案原點 (0,0,0)，跟構件實際位置無關，
            // 整棟偏移時定位點也不會跟著動。改用外框中心當位置
            BoundingBoxXYZ inPlaceBox = (el is FamilyInstance fiIp && fiIp.Symbol?.Family?.IsInPlace == true)
                ? el.get_BoundingBox(null) : null;

            if (inPlaceBox != null)
            {
                SetCenter(snap, inPlaceBox);
                snap.Rotation = 0;
            }
            else if (el.Location is LocationPoint lp)
            {
                snap.X = lp.Point.X * FT_TO_MM;
                snap.Y = lp.Point.Y * FT_TO_MM;
                snap.Z = lp.Point.Z * FT_TO_MM;

                // 柱的高度用外框中心（柱的定位點在柱底）
                if (bic == BuiltInCategory.OST_StructuralColumns)
                {
                    BoundingBoxXYZ bb = el.get_BoundingBox(null);
                    if (bb != null) snap.Z = ((bb.Min.Z + bb.Max.Z) / 2) * FT_TO_MM;
                }

                if (el is FamilyInstance fiLp)
                {
                    var transform = fiLp.GetTotalTransform();
                    snap.Rotation = Math.Atan2(transform.BasisX.Y, transform.BasisX.X) * (180.0 / Math.PI);
                }
                else
                {
                    snap.Rotation = lp.Rotation * (180.0 / Math.PI);
                }
            }
            else if (el.Location is LocationCurve lc)
            {
                XYZ p0 = lc.Curve.GetEndPoint(0), p1 = lc.Curve.GetEndPoint(1);
                snap.StartX = p0.X * FT_TO_MM; snap.StartY = p0.Y * FT_TO_MM; snap.StartZ = p0.Z * FT_TO_MM;
                snap.EndX = p1.X * FT_TO_MM; snap.EndY = p1.Y * FT_TO_MM; snap.EndZ = p1.Z * FT_TO_MM;
                XYZ mid = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2;

                snap.X = mid.X * FT_TO_MM;
                snap.Y = mid.Y * FT_TO_MM;
                snap.Z = mid.Z * FT_TO_MM;

                if (el is FamilyInstance fiLc)
                {
                    var transform = fiLc.GetTotalTransform();
                    snap.Rotation = Math.Atan2(transform.BasisX.Y, transform.BasisX.X) * (180.0 / Math.PI);
                }
                else
                {
                    XYZ dir = (lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0)).Normalize();
                    snap.Rotation = Math.Atan2(dir.Y, dir.X) * (180.0 / Math.PI);
                }
            }
            else
            {
                bool positioned = false;
                var geoElem = el.get_Geometry(new Options { ComputeReferences = false });

                if (geoElem != null)
                {
                    double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                    bool hasGeo = false;

                    foreach (GeometryObject geoObj in geoElem)
                    {
                        Solid solid = geoObj as Solid;
                        if (solid == null || solid.Volume <= 0) continue;

                        foreach (Edge edge in solid.Edges)
                        {
                            XYZ pt = edge.AsCurve().GetEndPoint(0);
                            minX = Math.Min(minX, pt.X); maxX = Math.Max(maxX, pt.X);
                            minY = Math.Min(minY, pt.Y); maxY = Math.Max(maxY, pt.Y);
                            minZ = Math.Min(minZ, pt.Z); maxZ = Math.Max(maxZ, pt.Z);
                            hasGeo = true;
                        }
                    }

                    if (hasGeo)
                    {
                        snap.X = ((minX + maxX) / 2) * FT_TO_MM;
                        snap.Y = ((minY + maxY) / 2) * FT_TO_MM;
                        snap.Z = ((minZ + maxZ) / 2) * FT_TO_MM;
                        positioned = true;
                    }
                }

                // 族群（例如內建族群的梁）、樓梯、欄杆的幾何常包在 GeometryInstance 裡，
                // 上面抓不到實體，改用外框中心
                if (!positioned)
                {
                    BoundingBoxXYZ bbox = el.get_BoundingBox(null);
                    if (bbox != null) SetCenter(snap, bbox);
                }

                if (el is FamilyInstance fiElse)
                {
                    var transform = fiElse.GetTotalTransform();
                    snap.Rotation = Math.Atan2(transform.BasisX.Y, transform.BasisX.X) * (180.0 / Math.PI);
                }
            }
        }

        // 需要比對的參數：學生常畫錯、但位置和類型名稱看不出來的地方
        private static void ReadProperties(Document doc, Element el, BuiltInCategory bic, ElementSnapshot snap)
        {
            if (el is Wall wall)
            {
                PutParamMM(snap, el, BuiltInParameter.WALL_USER_HEIGHT_PARAM, "\u7246\u9ad8");
                snap.Nums["\u539a\u5ea6"] = Math.Round(wall.Width * FT_TO_MM, 1);
            }
            else if (el is Floor)
            {
                PutParamMM(snap, el, BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM, "\u539a\u5ea6");
            }
            else if (el is FamilyInstance fi &&
                     (bic == BuiltInCategory.OST_Doors || bic == BuiltInCategory.OST_Windows))
            {
                // 門開向畫反、左右開反：位置和類型都一樣，只有翻轉狀態不同
                snap.Texts["\u5167\u5916\u7ffb\u8f49"] = fi.FacingFlipped ? "\u662f" : "\u5426";
                snap.Texts["\u5de6\u53f3\u7ffb\u8f49"] = fi.HandFlipped ? "\u662f" : "\u5426";
            }
            else if (el is Stairs st)
            {
                ReadStairs(doc, st, snap);
            }
            else if (el is Railing)
            {
                if (doc.GetElement(el.GetTypeId()) is RailingType rt)
                    snap.Nums["\u6276\u624b\u9ad8\u5ea6"] = Math.Round(rt.TopRailHeight * FT_TO_MM, 1);
                var bb = el.get_BoundingBox(null);
                if (bb != null)
                {
                    snap.BoxMinX = bb.Min.X * FT_TO_MM; snap.BoxMinY = bb.Min.Y * FT_TO_MM; snap.BoxMinZ = bb.Min.Z * FT_TO_MM;
                    snap.BoxMaxX = bb.Max.X * FT_TO_MM; snap.BoxMaxY = bb.Max.Y * FT_TO_MM; snap.BoxMaxZ = bb.Max.Z * FT_TO_MM;
                }
            }
        }

        private static void ReadStairs(Document doc, Stairs st, ElementSnapshot snap)
        {
            snap.Nums["\u7d1a\u9ad8"] = Math.Round(st.ActualRiserHeight * FT_TO_MM, 1);
            snap.Nums["\u7d1a\u6df1"] = Math.Round(st.ActualTreadDepth * FT_TO_MM, 1);
            snap.Nums["\u5be6\u969b\u968e\u6578"] = st.ActualRisersNumber;
            snap.Nums["\u6240\u9700\u968e\u6578"] = st.DesiredRisersNumber;
            PutParamMM(snap, st, BuiltInParameter.STAIRS_BASE_OFFSET, "\u5e95\u90e8\u504f\u79fb");
            PutParamMM(snap, st, BuiltInParameter.STAIRS_TOP_OFFSET, "\u9802\u90e8\u504f\u79fb");
            snap.Texts["\u5e95\u90e8\u6a13\u5c64"] = LevelName(doc, st, BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
            snap.Texts["\u9802\u90e8\u6a13\u5c64"] = LevelName(doc, st, BuiltInParameter.STAIRS_TOP_LEVEL_PARAM);

            // 梯段：依高度排序，逐段記寬度與豎板起訖設定
            var runs = st.GetStairsRuns().Select(id => doc.GetElement(id) as StairsRun)
                .Where(r => r != null).OrderBy(r => r.BaseElevation).ToList();
            snap.Nums["\u68af\u6bb5\u6578"] = runs.Count;
            for (int i = 0; i < runs.Count; i++)
            {
                snap.Nums[$"\u68af\u6bb5{i + 1}\u5bec\u5ea6"] = Math.Round(runs[i].ActualRunWidth * FT_TO_MM, 1);
                snap.Texts[$"\u68af\u6bb5{i + 1}\u4ee5\u8c4e\u677f\u958b\u59cb"] = runs[i].BeginsWithRiser ? "\u662f" : "\u5426";
                snap.Texts[$"\u68af\u6bb5{i + 1}\u4ee5\u8c4e\u677f\u7d50\u675f"] = runs[i].EndsWithRiser ? "\u662f" : "\u5426";
            }
            snap.Nums["\u5e73\u53f0\u6578"] = st.GetStairsLandings().Count;

            // 樓梯方向：第一段梯段的行進方向（上樓方向畫反、整座轉錯方向都會改變它）
            if (runs.Count > 0)
            {
                foreach (Curve c in runs[0].GetStairsPath())
                {
                    XYZ d = c.GetEndPoint(1) - c.GetEndPoint(0);
                    snap.Rotation = Math.Atan2(d.Y, d.X) * (180.0 / Math.PI);
                    break;
                }
            }

            // 類型內容：學生常直接改類型參數卻沒改類型名稱
            if (doc.GetElement(st.GetTypeId()) is StairsType stype)
            {
                snap.Nums["\u6700\u5927\u7d1a\u9ad8"] = Math.Round(stype.MaxRiserHeight * FT_TO_MM, 1);
                snap.Nums["\u6700\u5c0f\u7d1a\u6df1"] = Math.Round(stype.MinTreadDepth * FT_TO_MM, 1);
                if (doc.GetElement(stype.LandingType) is StairsLandingType lt)
                    snap.Nums["\u5e73\u53f0\u539a\u5ea6"] = Math.Round(lt.Thickness * FT_TO_MM, 1);
            }
        }

        // 依附的主體（牆上的門窗、樓梯上的扶手、樓板上的開口…）；依附在樓層、參考平面上的不算
        private static long HostIdOf(Element el)
        {
            if (el is FamilyInstance fi && fi.Host != null && !(fi.Host is Level)) return fi.Host.Id.Value;
            if (el is Railing r && r.HostId != ElementId.InvalidElementId) return r.HostId.Value;
            if (el is Opening op && op.Host != null) return op.Host.Id.Value;
            return 0;
        }

        private static string LevelName(Document doc, Element el, BuiltInParameter bip)

        {
            var p = el.get_Parameter(bip);
            if (p == null || !p.HasValue || p.StorageType != StorageType.ElementId) return "";
            return doc.GetElement(p.AsElementId())?.Name ?? "";
        }

        private static void PutParamMM(ElementSnapshot snap, Element el, BuiltInParameter bip, string key)
        {
            Parameter p = el.get_Parameter(bip);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                snap.Nums[key] = Math.Round(p.AsDouble() * FT_TO_MM, 1);
        }

        private static void SetCenter(ElementSnapshot snap, BoundingBoxXYZ bb)
        {
            snap.X = ((bb.Min.X + bb.Max.X) / 2) * FT_TO_MM;
            snap.Y = ((bb.Min.Y + bb.Max.Y) / 2) * FT_TO_MM;
            snap.Z = ((bb.Min.Z + bb.Max.Z) / 2) * FT_TO_MM;
        }

        // 依高度找構件所在樓層：取「高度 <= 構件中心 + 0.5m」的最高樓層。
        // 多 0.5m 是因為梁、樓板的中心略低於它所屬的樓層線
        private static Level LevelAtHeight(List<Level> levels, double zFt)
        {
            if (levels.Count == 0) return null;
            Level found = levels[0];
            foreach (var lv in levels)
            {
                if (lv.ProjectElevation <= zFt + 500 / FT_TO_MM) found = lv;
                else break;
            }
            return found;
        }

        private static double GetParamMM(Element el, BuiltInParameter bip)
        {
            Parameter p = el.get_Parameter(bip);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                return p.AsDouble() * FT_TO_MM;
            return 0;
        }
    }
}