using StatePipes.Comms;
using StatePipes.Comms.Internal;
using System;
using System.Threading;

namespace StatePipes.BrokerProxy
{
    /// <summary>
    /// Picks the <see cref="ISimpleConnectionChannel"/> implied by <see cref="BusConfig.BrokerUri"/>, so callers
    /// never reference a concrete channel. Plays the same role as <see cref="TransportFactory"/> but builds the
    /// proxy's own low-latency channels instead of full transports.
    ///
    /// <para>Selection reads <see cref="BusConfig.TransportKind"/> so the rule cannot drift from the one
    /// StatePipes uses. That matters: the services on either side derive their transport from the BrokerUri in
    /// the StatePipesReplyTo header, and if the rules disagreed a reflected message would be published on a
    /// transport nobody is listening to.</para>
    ///
    /// <para>The source and proxy sides are resolved independently, so the proxy can bridge RabbitMQ to Kafka
    /// (or either to itself) purely from how SOURCEBROKER and PROXYBROKER are spelled.</para>
    /// </summary>
    internal static class SimpleConnectionChannelFactory
    {
        public static ISimpleConnectionChannel Create(BusConfig busConfig, string? hashedPassword, Action<ISimpleConnectionChannel>? configureBuses = null, CancellationToken cancelToken = default)
            => busConfig.TransportKind switch
            {
                TransportKind.RabbitMq => new RabbitMqConnectionChannel(busConfig, hashedPassword, configureBuses, cancelToken),
                TransportKind.Kafka => new KafkaConnectionChannel(busConfig, hashedPassword, configureBuses, cancelToken),
                _ => throw new ArgumentOutOfRangeException(nameof(busConfig), busConfig.TransportKind, "Unknown transport kind.")
            };
    }
}
