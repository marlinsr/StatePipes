using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace StatePipes.Common.Internal
{
    internal class StatePipesJsonConverters
    {
        private static IReadOnlyList<JsonConverter> _converters = new JsonConverter[]
        {
                new StatePipesJsonConverter<IReadOnlyList<byte>>(o => JsonConvert.SerializeObject((o ?? Array.Empty<byte>()).ToArray(), Formatting.None), s => JsonConvert.DeserializeObject<byte[]>(s ?? "\"\\\"\\\"\"")),
                new StatePipesJsonConverter<List<byte>>(o => JsonConvert.SerializeObject((o ?? new List<byte>()).ToArray(), Formatting.None), s => (JsonConvert.DeserializeObject<byte[]>(s ?? "\"\\\"\\\"\"")?.ToList())),
                new StringEnumConverter()
        };
        public static JsonConverter[] Converters => _converters.ToArray();
    }
}
