using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace RevitGradingTool
{
    public class HighlightManager
    {
        private Document _doc;
        private List<ElementId> _highlightedIds = new List<ElementId>();

        public HighlightManager(Document doc)
        {
            _doc = doc;
        }

        // 清除所有標色
        public void ClearAll()
        {
            using (var tx = new Transaction(_doc, "清除標色"))
            {
                tx.Start();
                foreach (var id in _highlightedIds)
                {
                    var el = _doc.GetElement(id);
                    if (el != null)
                        _doc.ActiveView.SetElementOverrides(id, new OverrideGraphicSettings());
                }
                tx.Commit();
            }
            _highlightedIds.Clear();
        }

        // 標色一位學生的錯誤
        public void Highlight(List<CompareError> errors, ModelSnapshot standard)
        {
            ClearAll();

            using (var tx = new Transaction(_doc, "標色學生錯誤"))
            {
                tx.Start();

                foreach (var err in errors)
                {
                    // 在標準模型座標附近找對應的 Revit 構件
                    var collector = new FilteredElementCollector(_doc)
                        .WhereElementIsNotElementType();

                    foreach (Element el in collector)
                    {
                        XYZ elPt = GetElementCenter(el);
                        if (elPt == null) continue;

                        double dist = elPt.DistanceTo(
                            new XYZ(err.X / 304.8, err.Y / 304.8, err.Z / 304.8));

                        if (dist > 50.0 / 304.8) continue;

                        var ovr = new OverrideGraphicSettings();

                        if (err.Type == ErrorType.Missing)
                        {
                            // 紅色：缺少
                            ovr.SetSurfaceForegroundPatternColor(new Color(255, 0, 0));
                            ovr.SetSurfaceForegroundPatternId(GetSolidFillId());
                        }
                        else if (err.Type == ErrorType.Extra)
                        {
                            // 藍色：多餘
                            ovr.SetSurfaceForegroundPatternColor(new Color(0, 100, 255));
                            ovr.SetSurfaceForegroundPatternId(GetSolidFillId());
                        }
                        else
                        {
                            // 橘色：屬性錯誤
                            ovr.SetSurfaceForegroundPatternColor(new Color(255, 140, 0));
                            ovr.SetSurfaceForegroundPatternId(GetSolidFillId());
                        }

                        _doc.ActiveView.SetElementOverrides(el.Id, ovr);
                        _highlightedIds.Add(el.Id);
                        break;
                    }
                }

                tx.Commit();
            }
        }

        private XYZ GetElementCenter(Element el)
        {
            if (el.Location is LocationPoint lp)
                return lp.Point;
            if (el.Location is LocationCurve lc)
                return (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2;
            return null;
        }

        private ElementId GetSolidFillId()
        {
            var patterns = new FilteredElementCollector(_doc)
                .OfClass(typeof(FillPatternElement));
            foreach (FillPatternElement fp in patterns)
            {
                if (fp.GetFillPattern().IsSolidFill)
                    return fp.Id;
            }
            return ElementId.InvalidElementId;
        }
    }
}