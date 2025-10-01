using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using StatePipes.Common.Internal;
using System.Reflection;

namespace StatePipes.Common
{
    public static class JsonUtility
    {
        public static dynamic? GetObjectFromJson(string jsonString, Type t)
        {
            var parameterTypes = new[] { typeof(string), typeof(JsonConverter[]) };
            var deserializeMethod = typeof(JsonConvert).GetMethods(BindingFlags.Public | BindingFlags.Static).ToList()
                .Where(i => i.Name.Equals("DeserializeObject", StringComparison.InvariantCulture))
                .Where(i => i.IsGenericMethod)
                .Where(i => i.GetParameters().Select(a => a.ParameterType).SequenceEqual(parameterTypes))
                .Single();
            var deserializeMethodOfThisType = deserializeMethod.MakeGenericMethod(new[] { t });
            return deserializeMethodOfThisType.Invoke(null, new object[] { jsonString, StatePipesJsonConverters.Converters });
        }
        public static T Clone<T>(T obj) where T : class => GetObjectForJsonString<T>(GetJsonStringForObject(obj))!;

        public static dynamic? CloneObject(object? obj)
        {
            if (obj == null) return null;
            GetObjectFromJson(JsonConvert.SerializeObject(obj, Formatting.Indented, StatePipesJsonConverters.Converters), obj.GetType());
            return obj;
        }
        public static T? CloneToType<T>(object? obj) => (T?)CloneObject(obj);
        public static string GetJsonStringForObject(object? obj, bool noForatting = false)
        {
            var converter = new StringEnumConverter();
            return noForatting ? JsonConvert.SerializeObject(obj, Formatting.None, converter): JsonConvert.SerializeObject(obj, Formatting.Indented, converter);
        }
        public static T? GetObjectForJsonString<T>(string jsonString) where T : class => JsonConvert.DeserializeObject<T>(jsonString, StatePipesJsonConverters.Converters);
    }
}
