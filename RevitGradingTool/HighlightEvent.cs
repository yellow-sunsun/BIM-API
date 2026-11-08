using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;

namespace RevitGradingTool
{
    public class HighlightEvent : IExternalEventHandler
    {
        public List<CompareError> Errors { get; set; }
        public ModelSnapshot Standard { get; set; }
        private Document _doc;

        public void Execute(UIApplication app)
        {
            _doc = app.ActiveUIDocument.Document;
            var manager = new HighlightManager(_doc);
            manager.Highlight(Errors, Standard);
        }

        public string GetName() => "HighlightEvent";
    }
}