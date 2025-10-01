using Microsoft.AspNetCore.Components;
using StatePipes.Common;
using StatePipes.Explorer.NonWebClasses;
namespace StatePipes.Explorer.Components.Pages
{
    public partial class EventViewer
    {
        [Parameter]
        public Guid InstanceGuid { get; set; } = Guid.Empty;
        [Parameter]
        public EventEntry? Evnt { get; set; }
        [Parameter]
        public string TitleColor { get; set; } = "darkblue";
        [Parameter]
        public string? QuickViewUriPrefix { get; set; }
        [Parameter]
        public ExcludeAndIncludeLists? ObjectEditorFilter { get; set; }
        [Parameter]
        public ExcludeAndIncludeLists? ShowFilter { get; set; }
        private string QuickViewString
        {
            get
            {
                ExcludeAndIncludeLists filter = new();
                filter.Exclude.Add(ExcludeAndIncludeLists.ExcludeAllString);
                filter.Include.Add(Evnt?.FullName!);
                var ret = $"{QuickViewUriPrefix}_{Evnt?.FullName}/{filter.GetJsonString()}";
                if (!(ObjectEditorFilter?.IsIncluded(Evnt?.FullName!) ?? true))
                {
                    ExcludeAndIncludeLists objFilter = new();
                    objFilter.Exclude.Add(Evnt?.FullName!);
                    ret += $"/{objFilter.GetJsonString()}";
                }
                return ret;
            }
        }
        private string Title
        {
            get
            {
                if (Evnt == null) return string.Empty;
                int indx = Evnt.FullName.LastIndexOf(".");
                if (indx < 0) return Evnt.FullName;
                return Evnt.FullName.Substring(indx + 1);
            }
        }
        private bool ShowObjectEditor = true;
        private string EventObjectString = string.Empty;
        private ClassViewer _classEditor = default!;
        private PropertyValueClass? EventObject;
        private EventEntry? _evnt;
        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            var tempEvent = GetEventObject();
            if (EventObject != tempEvent)
            {
                _evnt = Evnt;
                EventObject = tempEvent;
                UpdateJson();
            }
        }
        private PropertyValueClass? GetEventObject()
        {
            if (Evnt == null) return null;
            if (Evnt.Obj == null) return null;
            return PropertyEntityViewer.GetPropertyValueClass(InstanceGuid, Evnt.FullName, null, Evnt.Obj.GetType(), Evnt.Obj, true);
        }
        private void ToggleDisplayMode(bool showObjectEditor)
        {
            if (ShowObjectEditor == showObjectEditor) return;
            ShowObjectEditor = showObjectEditor;
            if (!ShowObjectEditor)
            {
                UpdateJson();
            }
        }
        private void UpdateJson()
        {
            if (!ShowObjectEditor || !(ObjectEditorFilter?.IsIncluded(Evnt?.FullName!) ?? true))
            {
                EventObjectString = JsonUtility.GetJsonStringForObject(Evnt?.Obj);
            }
        }
    }
}
