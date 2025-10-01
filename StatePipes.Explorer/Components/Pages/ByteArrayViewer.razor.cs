using StatePipes.Common;
using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class ByteArrayViewer
    {
        private string? EditorObjectString;
        private PropertyValueClass? _previousEditorObject;
        private const string _typeName = "Byte[]";
        protected override void OnParametersSet()
        {
            if (_previousEditorObject != EditorObject)
            {
                if (EditorObject?.Value == null)
                {
                    EditorObjectString = "";
                }
                else
                {
                    EditorObjectString = JsonUtility.GetJsonStringForObject(EditorObject.Value, true);
                }
            }
            _previousEditorObject = EditorObject;
        }
        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            string name = string.Empty;

            if (!string.IsNullOrEmpty(EditorObjectString))
            {
                if (getName && EditorObject?.Name != null)
                {
                    jsonStringBuilder.Append($"\"{EditorObject.Name}\": ");
                }
                if (string.IsNullOrEmpty(EditorObjectString))
                {
                    jsonStringBuilder.Append("\"\\\"\\\"\"");
                }
                else
                {
                    jsonStringBuilder.Append(EditorObjectString);
                }
                return true;
            }
            return false;
        }
    }
}
