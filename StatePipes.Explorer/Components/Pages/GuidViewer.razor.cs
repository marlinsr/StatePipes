using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class GuidViewer
    {
        private string? EditorObjectString;
        private PropertyValueClass? PreviousEditorObject;
        private string NewGuidString = string.Empty;
        private string BorderColor = "black";

        private void Commit()
        {
            try
            {
                var guid = Guid.Parse(NewGuidString);
                EditorObjectString = guid.ToString();
                BorderColor = "black";
                StateHasChanged();
            }
            catch
            {
                BorderColor = "red";
                StateHasChanged();
            }
        }

        private void NewGuid()
        {
            NewGuidString = Guid.NewGuid().ToString();
        }
        private void EmptyGuid()
        {
            NewGuidString = Guid.Empty.ToString();
        }

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
