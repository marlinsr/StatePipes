using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class PrimitiveArrayViewer
    {
        private string EditorObjectString = string.Empty;
        private PropertyValueClass? _previousEditorObject;
        private string _typeName = string.Empty;
        private bool _nullable;
        private IPrimitiveWorker? _primitiveWorker;
        protected override void OnParametersSet()
        {
            if (_previousEditorObject != EditorObject)
            {
                _nullable = EditorObject?.Nullable ?? false;
                SetupObject<uint>(false);
                SetupObject<long>(true);
                SetupObject<ulong>(false);
                SetupObject<sbyte>(true);
                SetupObject<short>(true);
                SetupObject<int>(true);
                SetupObject<char>(false);
                SetupObject<ushort>(false);
                SetupObject<double>(false);
                SetupObject<float>(false);
            }
            _previousEditorObject = EditorObject;
        }
        private void SetupObject<T>(bool signed)
        {
            if (EditorObject?.PropertyType?.UnderlyingSystemType?.GenericTypeArguments[0]?.FullName != typeof(T).FullName) return;
            _typeName = typeof(T).Name + " Collection";
            bool _elementIsNullable = EditorObject?.PropertyType?.FullName != typeof(T?[]).FullName;
            _primitiveWorker = new PrimitiveWorker<T>();
            if (_nullable && EditorObject?.Value == null)
            {
                EditorObjectString = string.Empty;
                return;
            }
            else
            {
                var arrayElements = EditorObject!.Value as ICollection<T>;
                if (arrayElements == null)
                {
                    EditorObjectString = string.Empty;
                    return;
                }
                StringBuilder builder = new StringBuilder();
                bool isNotFirstElement = false;
                foreach (var p in arrayElements)
                {
                    if (isNotFirstElement) builder.Append(", ");
                    if (p != null) builder.Append(p.ToString());
                    isNotFirstElement = true;
                }
                EditorObjectString = builder.ToString();
            }
        }
        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            if (EditorObject == null) return false;
            if (getName && EditorObject?.Name != null)
            {
                jsonStringBuilder.Append($"\"{EditorObject.Name}\": ");
            }
            jsonStringBuilder.Append($"[");
            if (!string.IsNullOrEmpty(EditorObjectString))
            {
                jsonStringBuilder.Append(EditorObjectString);
            }
            jsonStringBuilder.Append("]");
            return true;
        }
    }
}
