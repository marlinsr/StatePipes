namespace StatePipes.Comms.Internal
{
    /// <summary>
    /// Transport-neutral constants shared by consumers and transport implementations.
    /// Kept independent of any broker-specific type so neutral code does not have to
    /// reference a RabbitMQ-flavored class (e.g. <see cref="StatePipesConnectionFactory"/>).
    /// </summary>
    internal static class TransportConstants
    {
        public const int HeartbeatIntervalMilliseconds = 1000;
        /// <summary>
        /// A <see cref="BusConfig.BrokerUri"/> beginning with this scheme (case-insensitive)
        /// selects <see cref="TransportKind.Kafka"/>; anything else selects
        /// <see cref="TransportKind.RabbitMq"/>.
        /// </summary>
        public const string KafkaBrokerUriPrefix = "ssl://";
    }
}
