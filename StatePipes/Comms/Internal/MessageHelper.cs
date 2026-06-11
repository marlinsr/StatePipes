using StatePipes.Common;
using StatePipes.Common.Internal;
using System.Text;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.Comms.Internal
{
    internal class MessageHelper
    {
        public const string StatePipesReplyToHeader = "StatePipesReplyTo";
        internal static void Serialize(object message, BusConfig busConfig, out byte[] body, out IDictionary<string, object?> headers)
        {
            headers = new Dictionary<string, object?>
            {
                { StatePipesReplyToHeader, JsonUtility.GetJsonStringForObject(busConfig, true) }
            };
            var eventJson = JsonUtility.GetJsonStringForObject(message, true);
            body = Encoding.UTF8.GetBytes(eventJson);
        }

        internal static void Deserialize(ReceivedTransportMessage received, out object? message, out BusConfig? busConfig, TypeDictionary typeRepo)
        {
            message = null;
            busConfig = null;
            if (string.IsNullOrEmpty(received.Type))
            {
                Log?.LogError("Received command with no Type information.");
                return;
            }
            if (received.Headers == null || !received.Headers.TryGetValue(StatePipesReplyToHeader, out object? value) || value == null)
            {
                Log?.LogError($"Received command with no {StatePipesReplyToHeader} information.");
                return;
            }
            var cmdJson = Encoding.UTF8.GetString(received.Body);
            var t = typeRepo.Get(received.Type);
            if (t == null)
            {
                Log?.LogError($"Unknown message type: {received.Type}");
                return;
            }
            message = JsonUtility.GetObjectFromJson(cmdJson, t);
            if (message == null)
            {
                Log?.LogError("Failed to deserialize message.");
                return;
            }
            busConfig = JsonUtility.GetObjectForJsonString<BusConfig>(Encoding.UTF8.GetString((byte[])value!));
            if (busConfig == null)
            {
                Log?.LogError($"Failed to deserialize BusConfig from {StatePipesReplyToHeader} property for message.");
                return;
            }
        }
    }
}
