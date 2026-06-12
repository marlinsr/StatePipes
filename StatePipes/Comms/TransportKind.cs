namespace StatePipes.Comms
{
    /// <summary>
    /// Selects which message-broker transport a <see cref="BusConfig"/> uses.
    /// Defaults to <see cref="RabbitMq"/> (value 0) so configs and serialized messages
    /// that predate this field deserialize to RabbitMQ.
    /// </summary>
    public enum TransportKind
    {
        RabbitMq = 0,
        Kafka = 1
    }
}
