using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace StatePipes.ServiceCreatorTool
{
    internal class ToolConfigurationUtility
    {
        public static ToolConfiguration? ReadConfiguration(string solutionTopLevelPath)
        {
            var jsonString = System.IO.File.ReadAllText(solutionTopLevelPath + $"\\{typeof(ToolConfiguration).Name}.json");
            return JsonConvert.DeserializeObject<ToolConfiguration>(jsonString, new StringEnumConverter());
        }
        public static void WriteConfiguration(string solutionTopLevelPath, ToolConfiguration configuration) 
        {
            var jsonString = JsonConvert.SerializeObject(configuration, Formatting.Indented, new StringEnumConverter());
            System.IO.File.WriteAllText(solutionTopLevelPath + $"\\{typeof(ToolConfiguration).Name}.json", jsonString);
        }
    }
}
