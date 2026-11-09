using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace RevitGradingTool
{
    public class HighlightManager
    {
        private Document _doc;
        private List<ElementId> _highlightedIds = new List<ElementId>();
        private List<ElementId> _textNoteIds = new List<ElementId>();

        public HighlightManager(Document doc)
        {
            _doc = doc;
        }

        public void ClearAll()
        {
            using (var tx = new Transaction(_doc, "清除標色"))
            {
                tx.Start();

                // 清除構件標色
                foreach (var id in _highlightedIds)
                {
                    var el = _doc.GetElement(id);
                    if (el != null)
                        _doc.ActiveView.SetElementOverrides(id, new OverrideGraphicSettings());
                }

                // 刪除 TextNote
                foreach (var id in _textNoteIds)
                {
                    var el = _doc.GetElement(id);
                    if (el != null)
                        _doc.Delete(id);
                }

                tx.Commit();
            }

            _highlightedIds.Clear();
            _textNoteIds.Clear();
        }

        public void Highlight(List<CompareError> errors, ModelSnapshot standard)
        {
            ClearAll();

            using (var tx = new Transaction(_doc, "標色學生錯誤"))
            {
                tx.Start();

                // 取得 TextNote 類型 Id
                var textNoteTypeId = _doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

                foreach (var err in errors)
                {
                    XYZ position = new XYZ(err.X / 304.8, err.Y / 304.8, err.Z / 304.8);

                    if (err.Type == ErrorType.Extra)
                    {
                        // 多餘構件：在座標位置放箭頭文字標記
                        var textNoteOptions = new TextNoteOptions
                        {
                            HorizontalAlignment = HorizontalTextAlignment.Center,
                            TypeId = textNoteTypeId
                        };

                        TextNote note = TextNote.Create(
                            _doc,
                            _doc.ActiveView.Id,
                            position,
                            "\u2b06 \u591a\u9918\u69cb\u4ef6",
                            textNoteOptions);

                        _textNoteIds.Add(note.Id);
                    }
                    else
                    {
                        // 缺少 / 屬性錯誤：在標準模型上找對應構件標色
                        var collector = new FilteredElementCollector(_doc)
                            .WhereElementIsNotElementType();

                        foreach (Element el in collector)
                        {
                            XYZ elPt = GetElementCenter(el);
                            if (elPt == null) continue;

                            double dist = elPt.DistanceTo(position);
                            if (dist > 50.0 / 304.8) continue;

                            var ovr = new OverrideGraphicSettings();

                            if (err.Type == ErrorType.Missing)
                            {
                                // 紅色：缺少
                                ovr.SetSurfaceForegroundPatternColor(new Color(255, 0, 0));
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
                }

                tx.Commit();
            }
        }

        private XYZ GetElementCenter(Element el)
        {
            if (el.Location is LocationPoint lp) return lp.Point;
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