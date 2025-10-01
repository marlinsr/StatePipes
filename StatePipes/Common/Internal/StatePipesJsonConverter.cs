using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StatePipes.Common.Internal
{
    internal class StatePipesJsonConverter<T>(Func<T?, string?> serializeFunc, Func<string?, T?> deserializeFunc) : JsonConverter<T>
    {
        private readonly Func<T, string?> _serializeFunc = serializeFunc;
        private readonly Func<string, T?> _deserializeFunc = deserializeFunc;
        public override void WriteJson(JsonWriter writer, T? value, JsonSerializer serializer)
        {
            if (value == null) return;
            string? s = _serializeFunc(value);
            if (s == null) return;
            JToken jObj = JToken.FromObject(s);
            jObj.WriteTo(writer);
        }
        public override T? ReadJson(JsonReader reader,
                  Type objectType,
                  T? existingValue,
                  bool hasExistingValue,
                  JsonSerializer serializer)
        {
            try
            {
                var s = (string?)reader.Value;
                if (s == null) return default;
                var obj = _deserializeFunc(s);
                return obj;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
