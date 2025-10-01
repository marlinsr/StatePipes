using Newtonsoft.Json;
using StatePipes.ProcessLevelServices;
using System.Reflection;
using System.Text;
namespace StatePipes.Explorer.Components.Pages
{
    public partial class ClassViewer
    {
        private Dictionary<string, PropertyValueClass>? PrimitiveProperties;

        private PropertyValueClass? PreviousEditorObject;

        protected override void OnParametersSet()
        {
            if (PreviousEditorObject != EditorObject) PrimitiveProperties = GetPrimitiveProperties();
            PreviousEditorObject = EditorObject;
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
                jsonAddedAlready |= editor.GetJson(jsonStringBuilder);
            }
            jsonStringBuilder.Append("}");
            return true;
        }

        private bool IsJsonIgnore(PropertyInfo p) => p.GetCustomAttributes(typeof(JsonIgnoreAttribute), false).Any();

        private Dictionary<string, PropertyValueClass> GetPrimitiveProperties()
        {
            Dictionary<string, PropertyValueClass> propertyNameValueDictionary = new();
            if (EditorObject?.Value == null) return propertyNameValueDictionary;
            Type editorObjectType = EditorObject.Value.GetType();
            var properties = editorObjectType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            if (properties.Length > 0)
            {
                foreach (PropertyInfo p in properties)
                {

                    if (p.Name != null && !propertyNameValueDictionary.ContainsKey(p.Name) && !IsJsonIgnore(p) && (p.CanRead || p.GetGetMethod(false) != null))
                    {
                        if (p.CanWrite
                             || p.GetSetMethod(false) != null
                             || editorObjectType.GetConstructors().Where(c => c.GetParameters()?.Where(prop => prop.Name != null && prop.ParameterType.FullName == p.PropertyType.FullName && p.Name.EndsWith(prop.Name, StringComparison.InvariantCultureIgnoreCase)).Any() ?? false).Any())
                        {
                            try
                            {
                                if (EditorObject != null)
                                {
                                    var propVal = PropertyEntityViewer.GetPropertyValueClass(EditorObject.InstanceGuid, EditorObject.CommandTypeFullName, p.Name, p.PropertyType, p.GetValue(EditorObject.Value), EditorObject?.IsFromEvent ?? true);
                                    if (propVal != null) propertyNameValueDictionary.Add(p.Name, propVal);
                                }
                            }
                            catch (Exception ex)
                            {
                                LoggerHolder.Log?.LogException(ex);
                                LoggerHolder.Log?.LogVerbose($"Exception displaying property {p.Name}\nRestriction, if you have a class that inherits from a dictionary,list,enumerable, hashlist, or array it will not display properly in the object editor\nUse Json View to view and edit after you reset");
                            }
                        }
                    }
                }
            }
            return propertyNameValueDictionary;
        }
    }
}
