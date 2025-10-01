using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class BoolViewer
    {
        private string? EditorObjectString;
        private PropertyValueClass? PreviousEditorObject;

        protected override void OnParametersSet()
        {
            if (PreviousEditorObject != EditorObject)
            {
                EditorObjectString = EditorObject == null ? "{null}" : EditorObject?.Value?.ToString() ?? "{null}";
            }
            PreviousEditorObject = EditorObject;
        }

        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            if (getName && EditorObject?.Name != null)
            {
                jsonStringBuilder.Append($"\"{EditorObject.Name}\": ");
            }
            jsonStringBuilder.Append($"\"{EditorObjectString}\"");
            return true;
        }
    }
}
