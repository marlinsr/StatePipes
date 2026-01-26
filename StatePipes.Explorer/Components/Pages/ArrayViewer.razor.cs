using StatePipes.Common;
using System.Collections;
using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class ArrayViewer
    {
        private List<PropertyValueClass>? ArrayElements;
        private PropertyValueClass? PreviousEditorObject;
        protected override void OnParametersSet()
        {
            if (PreviousEditorObject != EditorObject) ArrayElements = GetArrayElements();
            PreviousEditorObject = EditorObject;
        }
        private dynamic? CreateDefault(Type propertyType)
        {
            if (propertyType.IsGenericType && !propertyType.IsGenericTypeDefinition)
            {
                var genericTypeDef = propertyType.GetGenericTypeDefinition();
                if (typeof(List<>).FullName == genericTypeDef.FullName)
                {
                    dynamic? list = Activator.CreateInstance(propertyType);
                    if (list == null) return null;
                    list.Add(CreateDefault(propertyType.GetGenericArguments()[0]));
                    return list;
                }
            }
            return _statePipesHandler.TypeDefault(EditorObject!.InstanceGuid, EditorObject!.CommandTypeFullName, propertyType);
        }
        private void AddElement()
        {
            if (ArrayElements != null)
            {
                if (ArrayElements.Any())
                {
                    var lastElement = ArrayElements.Last();
                    if (lastElement.Value != null) ArrayElements.Add(new PropertyValueClass(EditorObject!.InstanceGuid, EditorObject?.CommandTypeFullName, $"Element {ArrayElements.Count}", JsonUtility.CloneObject(lastElement.Value), lastElement.PropertyTypeEnum, lastElement.PropertyType, lastElement.Nullable, EditorObject?.IsFromEvent ?? false));
                }
                else
                {
                    var elementType = EditorObject!.PropertyType!.GetGenericArguments()[0];
                    dynamic? p = CreateDefault(elementType);
                    var propertyValueClass = PropertyEntityViewer.GetPropertyValueClass(EditorObject!.InstanceGuid, EditorObject!.CommandTypeFullName, $"Element 0", elementType, p, EditorObject?.IsFromEvent ?? true);
                    if (propertyValueClass != null) ArrayElements.Add(propertyValueClass);
                }
            }

            StateHasChanged();
        }
        private void DeleteElement()
        {
            if (ArrayElements != null && ArrayElements.Any()) ArrayElements.Remove(ArrayElements.Last());
            StateHasChanged();
        }
        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            if (EditorObject == null) return false;
            if (getName && !string.IsNullOrEmpty(EditorObject?.Name)) jsonStringBuilder.Append($"{EditorObject?.Name}: ");
            jsonStringBuilder.Append("[");
            bool jsonAddedAlready = false;
            foreach (var editor in Editors)
            {
                if (jsonAddedAlready) jsonStringBuilder.Append(", ");
                jsonAddedAlready |= editor.GetJson(jsonStringBuilder, false);
            }
            jsonStringBuilder.Append("]");
            return true;
        }
        private List<PropertyValueClass> GetArrayElements()
        {
            List<PropertyValueClass> arrayElementsList = [];
            if (EditorObject?.Value == null) return arrayElementsList;
            var objListEnumerable = EditorObject.Value as IEnumerable;
            if (objListEnumerable == null) return arrayElementsList;
            int index = 0;
            foreach (object p in objListEnumerable)
            {
                var propertyValueClass = PropertyEntityViewer.GetPropertyValueClass(EditorObject!.InstanceGuid, EditorObject!.CommandTypeFullName, $"Element {index}", p.GetType(), p, EditorObject?.IsFromEvent ?? true);
                if (propertyValueClass != null) arrayElementsList.Add(propertyValueClass);
                index++;
            }
            return arrayElementsList;
        }
    }
}
