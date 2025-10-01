using StatePipes.Common;
using StatePipes.ExplorerTypes;
using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class ImageViewer
    {
        private PropertyValueClass? PreviousEditorObject;
        private string ImageHtml = "{null}";

        protected override void OnParametersSet()
        {
            if (PreviousEditorObject != EditorObject)
            {
                if (EditorObject == null || EditorObject.Value == null)
                {
                    ImageHtml = "{null}";
                }
                else
                {
                    var image = EditorObject.Value as SPEImage;
                    if (image == null)
                    {
                        ImageHtml = "{null}";
                    }
                    else
                    {
                        ImageHtml = $"data:image/{image.ImageType.ToString()};base64,{Convert.ToBase64String(image.ImageBytes.ToArray())}";
                    }
                }
            }
            PreviousEditorObject = EditorObject;
        }

        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            if (getName && EditorObject?.Name != null)
            {
                jsonStringBuilder.Append($"\"{EditorObject.Name}\": ");
            }
            if (EditorObject?.Value == null)
            {
                jsonStringBuilder.Append("{null}");
            }
            else
            {
                var editorObjectString = JsonUtility.GetJsonStringForObject(EditorObject?.Value);
                jsonStringBuilder.Append($"\"{editorObjectString}\"");
            }
            return true;
        }
    }
}
