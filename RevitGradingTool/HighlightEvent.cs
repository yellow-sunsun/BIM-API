using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;

namespace RevitGradingTool
{
    public class HighlightEvent : IExternalEventHandler
    {
        public List<CompareError> Errors { get; set; }
        public ModelSnapshot Standard { get; set; }

        private HighlightManager _manager;

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument.Document;

            if (_manager == null)
                _manager = new HighlightManager(doc);

            // 先清除上一位學生的標色，再標新的
            _manager.ClearAll();
            _manager.Highlight(Errors, Standard);
        }

        public string GetName() => "HighlightEvent";
    }
}