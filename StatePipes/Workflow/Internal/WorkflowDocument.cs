using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StatePipes.Workflow.Internal
{
    /// <summary>
    /// Moves every persisted state field into and out of the instance document:
    /// <c>{"states":{"&lt;StateName&gt;":{"&lt;field&gt;":value}}}</c> (spec §5.2).
    /// </summary>
    internal static class WorkflowDocument
    {
        public static string Extract(WorkflowMetadata metadata, IReadOnlyDictionary<string, object> statesByName, JsonSerializerSettings settings)
        {
            var states = new JObject();
            foreach (var (stateName, fields) in metadata.PersistedFields)
            {
                var state = statesByName[stateName];
                var stateObject = new JObject();
                foreach (var field in fields)
                {
                    try
                    {
                        stateObject[field.Name] = JToken.Parse(JsonConvert.SerializeObject(field.GetValue(state), field.FieldType, settings));
                    }
                    catch (Exception ex)
                    {
                        throw new WorkflowSerializationException($"{stateName}.{field.Name}: {ex.Message}", ex);
                    }
                }
                states[stateName] = stateObject;
            }
            return new JObject { ["states"] = states }.ToString(Formatting.None);
        }

        public static void Populate(WorkflowMetadata metadata, IReadOnlyDictionary<string, object> statesByName, string documentJson, JsonSerializerSettings settings)
        {
            var states = JObject.Parse(documentJson)["states"] as JObject;
            foreach (var (stateName, fields) in metadata.PersistedFields)
            {
                if (states?[stateName] is not JObject stateObject) continue;
                var state = statesByName[stateName];
                foreach (var field in fields)
                {
                    if (stateObject[field.Name] is not JToken token) continue;
                    try
                    {
                        field.SetValue(state, JsonConvert.DeserializeObject(token.ToString(Formatting.None), field.FieldType, settings));
                    }
                    catch (Exception ex)
                    {
                        throw new WorkflowSerializationException($"{stateName}.{field.Name}: {ex.Message}", ex);
                    }
                }
            }
        }
    }
}
