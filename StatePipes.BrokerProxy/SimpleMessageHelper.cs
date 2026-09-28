using Confluent.Kafka;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StatePipes.Common;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using System;
using System.Collections.Generic;
using System.Text;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.BrokerProxy
{
    /// <summary>
    /// Pulls the three things a reflected message needs off the wire, and puts them back on it.
    ///
    /// <para>Nothing here decodes a message. The payload stays an opaque buffer and the reply-to header stays
    /// raw UTF-8 JSON, so a reflected message never gets deserialized or re-serialized. The one exception is
    /// <see cref="ReadPreviousHop"/>, used only on the response path.</para>
    /// </summary>
    internal class SimpleMessageHelper
    {
        protected static string StatePipesReplyToHeader => MessageHelper.StatePipesReplyToHeader;

        /// <summary>
        /// Kafka has no AMQP <c>BasicProperties.Type</c>, so the message type name travels in this header.
        /// Matches StatePipes' KafkaTransport, which is what the services on either side speak.
        /// </summary>
        internal const string StatePipesTypeHeader = "StatePipesType";

        internal static void Serialize(string messageTypeFullName, ReadOnlyMemory<byte> replyToRaw, out BasicProperties properties)
        {
            properties = new BasicProperties();
            properties.Type = messageTypeFullName;
            // The header goes out as the bytes we were handed. RabbitMQ carries a byte[] header value as a byte
            // array field rather than a long string, which every StatePipes reader already handles: both
            // MessageHelper.Deserialize and this class read the value back as byte[].
            properties.Headers = new Dictionary<string, object?>
            {
                { StatePipesReplyToHeader, replyToRaw.ToArray() }
            };
        }

        internal static Confluent.Kafka.Headers SerializeKafkaHeaders(string messageTypeFullName, ReadOnlyMemory<byte> replyToRaw) =>
            new()
            {
                { StatePipesTypeHeader, Encoding.UTF8.GetBytes(messageTypeFullName) },
                { StatePipesReplyToHeader, replyToRaw.ToArray() }
            };

        internal static bool TryRead(BasicDeliverEventArgs ea, out byte[]? body, out string routingKey, out ReadOnlyMemory<byte> replyToRaw)
        {
            body = null;
            routingKey = string.Empty;
            replyToRaw = default;
            if (string.IsNullOrEmpty(ea.BasicProperties.Type))
            {
                Log?.LogError("Received command with no Type information.");
                return false;
            }
            routingKey = ea.BasicProperties.Type;
            if (ea.BasicProperties.Headers == null
                || !ea.BasicProperties.Headers.TryGetValue(StatePipesReplyToHeader, out var value)
                || value is not byte[] raw)
            {
                Log?.LogError($"Received command with no {StatePipesReplyToHeader} information.");
                return false;
            }
            replyToRaw = raw;
            body = ea.Body.ToArray();
            return true;
        }

        internal static bool TryRead(ConsumeResult<string, byte[]> result, out byte[]? body, out string routingKey, out ReadOnlyMemory<byte> replyToRaw)
        {
            body = null;
            routingKey = string.Empty;
            replyToRaw = default;
            byte[]? raw = null;
            foreach (var header in result.Message.Headers)
            {
                if (header.Key == StatePipesTypeHeader) routingKey = Encoding.UTF8.GetString(header.GetValueBytes());
                else if (header.Key == StatePipesReplyToHeader) raw = header.GetValueBytes();
            }
            if (string.IsNullOrEmpty(routingKey))
            {
                Log?.LogError("Received command with no Type information.");
                return false;
            }
            if (raw == null)
            {
                Log?.LogError($"Received command with no {StatePipesReplyToHeader} information.");
                return false;
            }
            replyToRaw = raw;
            body = result.Message.Value; // already a byte[] from librdkafka -- passed through, not copied
            return body != null;
        }

        /// <summary>
        /// The one place a reply-to header is actually parsed. Responses have to travel back to the hop that
        /// issued the command, and picking that destination means reading fields out of PreviousHop, which
        /// cannot be done by concatenation. Responses are one-per-command rather than the broadcast firehose,
        /// so the cost lands on the rare path and the guards in SendResponse stay straightforward.
        /// </summary>
        internal static BusConfig? ReadPreviousHop(ReadOnlyMemory<byte> replyToRaw)
        {
            var busConfig = JsonUtility.GetObjectForJsonString<BusConfig>(Encoding.UTF8.GetString(replyToRaw.Span));
            if (busConfig == null)
            {
                Log?.LogError($"Failed to deserialize BusConfig from {StatePipesReplyToHeader} property for message.");
                return null;
            }
            return busConfig.PreviousHop;
        }
    }
}
