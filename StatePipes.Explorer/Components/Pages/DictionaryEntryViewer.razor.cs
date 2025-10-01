using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class DictionaryEntryViewer
    {
        private Tuple<PropertyValueClass, PropertyValueClass>? DictionaryEntry;
        private PropertyValueClass? PreviousEditorObject;
        protected override void OnParametersSet()
        {
            if (PreviousEditorObject != EditorObject) DictionaryEntry = GetDictionaryEntry();
            PreviousEditorObject = EditorObject;
        }
        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            if (EditorObject == null) return false;
            var keyEditor = Editors.FirstOrDefault(t => t.EditorObject?.Name == "Key");
            var valEditor = Editors.FirstOrDefault(t => t.EditorObject?.Name == "Val");
            keyEditor?.GetJson(jsonStringBuilder, false);
            jsonStringBuilder.Append(": ");
            valEditor?.GetJson(jsonStringBuilder, false);
            return true;
        }
        private Tuple<PropertyValueClass, PropertyValueClass>? GetDictionaryEntry()
        {
            if (EditorObject?.Value == null) return null;
            var dictionaryEntry = EditorObject.Value as DictionaryEntryClass;
            if (dictionaryEntry == null) return null;
            var keyPropertyValueClass = PropertyEntityViewer.GetPropertyValueClass(EditorObject!.InstanceGuid, EditorObject!.CommandTypeFullName, "Key", dictionaryEntry.Key.GetType(), dictionaryEntry.Key, EditorObject?.IsFromEvent ?? true);
            var valPropertyValueClass = PropertyEntityViewer.GetPropertyValueClass(EditorObject!.InstanceGuid, EditorObject!.CommandTypeFullName, "Val", dictionaryEntry.Val.GetType(), dictionaryEntry.Val, EditorObject?.IsFromEvent ?? true);
            if (keyPropertyValueClass != null && valPropertyValueClass != null) return new Tuple<PropertyValueClass, PropertyValueClass>(keyPropertyValueClass, valPropertyValueClass);
            return null;
        }
    }
}
