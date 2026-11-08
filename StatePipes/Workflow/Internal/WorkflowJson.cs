using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;
using System.Reflection;

namespace StatePipes.Workflow.Internal
{
    internal sealed class WorkflowSerializationException(string message, Exception? inner = null) : Exception(message, inner);

    /// <summary>JSON settings for workflow documents and persisted timer messages (spec §5.3).</summary>
    internal static class WorkflowJson
    {
        public static JsonSerializerSettings CreateSettings(Assembly serviceAssembly) => new()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = new AllowListBinder(serviceAssembly),
            Converters = { new DelayedMessageSenderConverter() },
        };

        /// <summary>Only the service's own types, StatePipes, and BCL primitives/collections may appear as $type.</summary>
        private sealed class AllowListBinder(Assembly serviceAssembly) : ISerializationBinder
        {
            private readonly HashSet<Assembly> _allowed =
                [serviceAssembly, typeof(WorkflowJson).Assembly, typeof(object).Assembly, typeof(LinkedList<>).Assembly];

            private bool IsAllowed(Type type)
            {
                if (type.IsArray) return IsAllowed(type.GetElementType()!);
                if (!_allowed.Contains(type.Assembly)) return false;
                return !type.IsGenericType || type.GetGenericArguments().All(IsAllowed);
            }
            public Type BindToType(string? assemblyName, string typeName)
            {
                var type = Type.GetType(assemblyName == null ? typeName : $"{typeName}, {assemblyName}", throwOnError: false)
                    ?? throw new JsonSerializationException($"Type {typeName}, {assemblyName} could not be resolved.");
                if (!IsAllowed(type)) throw new JsonSerializationException($"Type {type.FullName} is not allowed in workflow data.");
                return type;
            }
            public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
            {
                if (!IsAllowed(serializedType)) throw new JsonSerializationException($"Type {serializedType.FullName} is not allowed in workflow data.");
                assemblyName = serializedType.Assembly.GetName().Name;
                typeName = serializedType.FullName;
            }
        }

        /// <summary>Persists an IDelayedMessageSender as its TimerId only; the countdown lives in the timer service.</summary>
        private sealed class DelayedMessageSenderConverter : JsonConverter
        {
            private static Type? MessageTypeOf(Type t)
            {
                if (!t.IsGenericType) return null;
                var def = t.GetGenericTypeDefinition();
                return def == typeof(IDelayedMessageSender<>) || def == typeof(DelayedMessageSender<>) ? t.GetGenericArguments()[0] : null;
            }
            public override bool CanConvert(Type objectType) => MessageTypeOf(objectType) != null;
            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            {
                if (value == null) { writer.WriteNull(); return; }
                var id = (Guid)value.GetType().GetProperty("TimerId")!.GetValue(value)!;
                new JObject { ["TimerId"] = id }.WriteTo(writer);
            }
            public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return null;
                var id = JObject.Load(reader)["TimerId"]!.ToObject<Guid>();
                var timers = WorkflowContext.Current?.Timers ?? throw new InvalidOperationException("No workflow context while reading a timer.");
                var senderType = typeof(DelayedMessageSender<>).MakeGenericType(MessageTypeOf(objectType)!);
                return Activator.CreateInstance(senderType, timers, id);
            }
        }
    }
}
