using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StatePipes.Common;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;
using System.Text;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.Comms.Internal
{
    internal class MessageHelper
    {
        const string StatePipesReplyToHeader = "StatePipesReplyTo";
        internal static void Serialize(IMessage message, BusConfig busConfig, out byte[] body, out BasicProperties properties)
        {
            properties = new BasicProperties();
            properties.Type = message.GetType().FullName;
            properties.Headers = new Dictionary<string, object?>
            {
                { StatePipesReplyToHeader, JsonUtility.GetJsonStringForObject(busConfig, true) }
            };
            var eventJson = JsonUtility.GetJsonStringForObject(message, true);
            body = Encoding.UTF8.GetBytes(eventJson);
        }

        internal static void Deserialize(BasicDeliverEventArgs ea, out IMessage? message, out BusConfig? busConfig, TypeDictionary typeRepo)
        {
            message = null;
            busConfig = null;
            if (string.IsNullOrEmpty(ea.BasicProperties.Type))
            {
                Log?.LogError("Received command with no Type information.");
                return;
            }
            if (ea.BasicProperties.Headers == null || !ea.BasicProperties.Headers.ContainsKey(StatePipesReplyToHeader) || ea.BasicProperties.Headers[StatePipesReplyToHeader] == null)
            {
                Log?.LogError($"Received command with no {StatePipesReplyToHeader} information.");
                return;
            }
            var cmdJson = Encoding.UTF8.GetString(ea.Body.ToArray());
            var t = typeRepo.Get(ea.BasicProperties.Type);
            if (t == null)
            {
                Log?.LogError($"Unknown message type: {ea.BasicProperties.Type}");
                return;
            }
            message = JsonUtility.GetObjectFromJson(cmdJson, t);
            if (message == null)
            {
                Log?.LogError("Failed to deserialize message.");
                return;
            }
            busConfig = JsonUtility.GetObjectForJsonString<BusConfig>(Encoding.UTF8.GetString((byte[])ea.BasicProperties.Headers[StatePipesReplyToHeader]!));
            if (busConfig == null)
            {
                Log?.LogError($"Failed to deserialize BusConfig from {StatePipesReplyToHeader} property for message.");
                return;
            }
        }
    }
}
