using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StatePipes.Common;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.BrokerProxy
{
    internal class SimpleMessageHelper
    {
        protected static string StatePipesReplyToHeader => MessageHelper.StatePipesReplyToHeader;
        internal static void Serialize(string messageTypeFullName, BusConfig busConfig, out BasicProperties properties)
        {
            properties = new BasicProperties();
            properties.Type = messageTypeFullName;
            properties.Headers = new Dictionary<string, object?>
            {
                { StatePipesReplyToHeader, JsonUtility.GetJsonStringForObject(busConfig, true) }
            };
        }

        internal static void Deserialize(BasicDeliverEventArgs ea, out byte[]? message, out string routingKey, out BusConfig? busConfig)
        {
            message = null;
            busConfig = null;
            routingKey = string.Empty;
            if (string.IsNullOrEmpty(ea.BasicProperties.Type))
            {
                Log?.LogError("Received command with no Type information.");
                return;
            }
            routingKey = ea.BasicProperties.Type;
            if (ea.BasicProperties.Headers == null || !ea.BasicProperties.Headers.ContainsKey(StatePipesReplyToHeader) || ea.BasicProperties.Headers[StatePipesReplyToHeader] == null)
            {
                Log?.LogError($"Received command with no {StatePipesReplyToHeader} information.");
                return;
            }
            message = ea.Body.ToArray();
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
