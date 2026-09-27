namespace StatePipes.ServiceCreatorTool
{
    /// <summary>
    /// The sliver of transport behavior the live proxy generator needs: publish one
    /// GetSelfDescriptionCommand and listen for the SelfDescriptionEvent that comes back.
    ///
    /// This is a deliberately tiny stand-in for StatePipes.Comms.Internal.ITransport. The tool does not
    /// reference the StatePipes assembly (it also carries its own <see cref="TypeSerializationList"/>),
    /// so the two transport flavors are re-implemented here with only that one request/response exchange
    /// supported -- no subscriptions, no heartbeats, no proxy plumbing.
    ///
    /// Destinations are named with RabbitMQ exchange names, matching how ITransport and BusConfig talk
    /// about them; the Kafka implementation maps those names onto topics the same way KafkaTopicNamer does.
    /// </summary>
    internal interface IServiceDescriptionTransport : IDisposable
    {
        /// <summary>
        /// Starts listening on the response destination. <paramref name="onMessageBody"/> is invoked with
        /// the raw body of each message that arrives, on a background thread.
        /// Call this before <see cref="SendGetSelfDescriptionCommand"/> so the reply cannot be missed.
        /// </summary>
        void ListenForResponses(string responseExchangeName, Action<byte[]> onMessageBody);

        /// <summary>Publishes the command. Returns false if it could not be handed to the broker.</summary>
        bool SendGetSelfDescriptionCommand(string commandExchangeName, string commandTypeName, byte[] replyToBytes);
    }
}
