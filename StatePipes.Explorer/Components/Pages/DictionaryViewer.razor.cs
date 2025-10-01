using StatePipes.Common;
using System.Collections;
using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class DictionaryViewer
    {
        private List<PropertyValueClass>? DictionaryElements;

        private PropertyValueClass? PreviousEditorObject;

        protected override void OnParametersSet()
        {
            if (PreviousEditorObject != EditorObject) DictionaryElements = GetDictionaryEntries();
            PreviousEditorObject = EditorObject;
        }

        private dynamic? CreateDefault(Type propertyType)
        {
            if (propertyType.IsGenericType && !propertyType.IsGenericTypeDefinition)
            {
                var genericTypeDef = propertyType.GetGenericTypeDefinition();
                if (typeof(Dictionary<,>).FullName == genericTypeDef.FullName)
                {
                    dynamic? dictionary = Activator.CreateInstance(propertyType);
                    dictionary!.Add(CreateDefault(propertyType.GetGenericArguments()[0]), CreateDefault(propertyType.GetGenericArguments()[1]));
                    return dictionary;
                }
            }
            return _statePipesHandler.TypeDefault(EditorObject!.InstanceGuid, EditorObject!.CommandTypeFullName, propertyType);
        }

        private void AddElement()
        {
            if (DictionaryElements != null)
            {
                if (DictionaryElements.Any())
                {
                    var lastElement = DictionaryElements.Last();
                    if (lastElement.Value != null) DictionaryElements.Add(new PropertyValueClass(lastElement!.InstanceGuid, lastElement!.CommandTypeFullName, lastElement?.Name, JsonUtility.Clone(lastElement!.Value), lastElement.PropertyTypeEnum, lastElement.PropertyType, lastElement.Nullable, EditorObject?.IsFromEvent ?? true));
                }
                else
                {
                    var keyType = EditorObject!.PropertyType!.GetGenericArguments()[0];
                    dynamic? key = CreateDefault(keyType);
                    var valType = EditorObject!.PropertyType!.GetGenericArguments()[1];
                    dynamic? val = CreateDefault(valType);
                    if (key == null || val == null) return;
                    var dictEntry = new DictionaryEntryClass(key, val);
                    var pvc = PropertyEntityViewer.GetPropertyValueClass(EditorObject!.InstanceGuid, EditorObject!.CommandTypeFullName, null, dictEntry.GetType(), dictEntry, EditorObject?.IsFromEvent ?? true);
                    if (pvc != null) DictionaryElements.Add(pvc);
                }
            }

            StateHasChanged();
        }

        private void DeleteElement()
        {
            if (DictionaryElements != null && DictionaryElements.Any())
            {
                DictionaryElements.Remove(DictionaryElements.Last());
            }

            StateHasChanged();
        }
        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            if (EditorObject == null) return false;
            if (getName && !string.IsNullOrEmpty(EditorObject?.Name))
            {
                jsonStringBuilder.Append($"{EditorObject?.Name}: ");
            }
            jsonStringBuilder.Append("{");
            bool jsonAddedAlready = false;
            foreach (var editor in Editors)
            {
                if (jsonAddedAlready) jsonStringBuilder.Append(", ");
                jsonAddedAlready |= editor.GetJson(jsonStringBuilder, false);
            }
            jsonStringBuilder.Append("}");

            return true;
        }

        private List<PropertyValueClass> GetDictionaryEntries()
        {
            List<PropertyValueClass> dictionaryEntries = new();
            if (EditorObject?.Value == null) return dictionaryEntries;
            var dictionary = EditorObject.Value as IDictionary;
            if (dictionary == null) return dictionaryEntries;
            foreach (object key in dictionary.Keys)
            {
                object val = dictionary[key]!;
                var dictEntry = new DictionaryEntryClass(key, val);
                var pvc = PropertyEntityViewer.GetPropertyValueClass(EditorObject!.InstanceGuid, EditorObject!.CommandTypeFullName, null, dictEntry.GetType(), dictEntry, EditorObject?.IsFromEvent ?? true);
                if (pvc != null) dictionaryEntries.Add(pvc);
            }
            return dictionaryEntries;
        }
    }
}
