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
    }
}
