namespace StatePipes.Comms.Internal
{
    /// <summary>
    /// Constructs the <see cref="ITransport"/> implementation selected by
    /// <see cref="BusConfig.TransportKind"/>. Centralizes transport instantiation so
    /// consumers never reference a concrete transport (e.g. <see cref="RabbitMqTransport"/>) directly.
    /// </summary>
    internal static class TransportFactory
    {
        public static ITransport Create(BusConfig busConfig, string? hashedPassword, Action<ITransport>? configureBuses = null, CancellationToken cancelToken = default)
            => busConfig.TransportKind switch
            {
                TransportKind.RabbitMq => new RabbitMqTransport(busConfig, hashedPassword, configureBuses, cancelToken),
                TransportKind.Kafka => new KafkaTransport(busConfig, hashedPassword, configureBuses, cancelToken),
                _ => throw new ArgumentOutOfRangeException(nameof(busConfig), busConfig.TransportKind, "Unknown transport kind.")
            };
    }
}
