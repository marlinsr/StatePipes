namespace StatePipes.Comms
{
    /// <summary>
    /// Identifies which message-broker transport a <see cref="BusConfig"/> uses. Never
    /// configured directly: <see cref="BusConfig.TransportKind"/> derives it from the
    /// broker URI scheme, so <see cref="RabbitMq"/> is what every non-Kafka URI yields.
    /// </summary>
    public enum TransportKind
    {
        RabbitMq = 0,
        Kafka = 1
    }
}
