namespace StatePipes.ServiceCreatorTool
{
    /// <summary>
    /// Constructs the <see cref="IServiceDescriptionTransport"/> implied by the broker URI, so callers never
    /// reference a concrete transport directly. Mirrors StatePipes.Comms.Internal.TransportFactory, and uses
    /// the same selection rule as BusConfig.TransportKind: a URI starting with <see cref="KafkaBrokerUriPrefix"/>
    /// is Kafka, anything else is RabbitMQ.
    ///
    /// Keeping the rule identical matters because the service on the other end re-derives the transport from
    /// the BrokerUri we hand it in the StatePipesReplyTo header -- if the two disagreed, the reply would be
    /// published on a transport we are not listening to.
    /// </summary>
    internal static class ServiceDescriptionTransportFactory
    {
        public const string KafkaBrokerUriPrefix = "ssl://";

        public static bool IsKafka(string brokerUri) =>
            brokerUri.StartsWith(KafkaBrokerUriPrefix, StringComparison.OrdinalIgnoreCase);

        public static IServiceDescriptionTransport Create(string brokerUri, string clientCertPath, string clientCertPasswordPath) =>
            IsKafka(brokerUri)
                ? new KafkaServiceDescriptionTransport(brokerUri, clientCertPath, clientCertPasswordPath)
                : new RabbitMqServiceDescriptionTransport(brokerUri, clientCertPath, clientCertPasswordPath);
    }
}
