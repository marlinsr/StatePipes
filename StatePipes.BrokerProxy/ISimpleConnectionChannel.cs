using StatePipes.Comms;
using StatePipes.Comms.Internal;
using System;
using System.Collections.Generic;

namespace StatePipes.BrokerProxy
{
    /// <summary>
    /// A message taken off a bus, handed over without being decoded.
    ///
    /// <para><paramref name="replyToRaw"/> is the <c>StatePipesReplyTo</c> header exactly as it arrived on the
    /// wire -- still UTF-8 JSON, never parsed into a BusConfig. <paramref name="body"/> is likewise the
    /// untouched payload. Reflecting a message is then a byte operation rather than a
    /// deserialize/re-serialize round trip (see <see cref="ReplyToEnvelope"/>).</para>
    ///
    /// <para>Raised synchronously on the transport's receive path, so the handler must not block.</para>
    /// </summary>
    internal delegate void SimpleMessageReceived(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw);

    /// <summary>
    /// The broker connection the proxy reflects messages across.
    ///
    /// <para>Deliberately NOT <see cref="ITransport"/>. BrokerProxy hand-rolls its comms so that reflecting a
    /// message costs a header concat and a publish, with no message type registry, handler dispatch or state
    /// machine in between. This interface exists only so the same reflecting logic can sit on either broker;
    /// it stops at "declare a bus" and "send bytes", and carries no broker types in its signatures.</para>
    ///
    /// <para>Bus names are RabbitMQ exchange names, matching how <see cref="BusConfig"/> talks about them.
    /// The Kafka implementation maps them onto topics with <see cref="KafkaTopicNamer"/>.</para>
    /// </summary>
    internal interface ISimpleConnectionChannel : IDisposable
    {
        static List<string> DefaultRoutingKeys { get; } = new List<string> { "#" };

        /// <summary>Whether the channel can currently carry messages. Callers check this before sending.</summary>
        bool IsOpen { get; }

        /// <summary>
        /// Declares a bus and, when <paramref name="consumeMethod"/> is supplied, starts consuming from it.
        /// <paramref name="routingKeys"/> filters by message type; <see cref="DefaultRoutingKeys"/> ("#") accepts all.
        /// </summary>
        void ConfigureBus(Guid id, CommunicationsType commsType, string exchangeName, SimpleMessageReceived? consumeMethod = null, List<string>? routingKeys = null, bool autoDelete = false);

        /// <summary>
        /// Publishes <paramref name="body"/> unchanged, writing <paramref name="replyToRaw"/> verbatim into the
        /// <c>StatePipesReplyTo</c> header. Nothing is serialized here. Fire and forget: never waits on the broker.
        /// </summary>
        void Send(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw, string exchangeName);
    }
}
