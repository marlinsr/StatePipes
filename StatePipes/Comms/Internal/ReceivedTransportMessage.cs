namespace StatePipes.Comms.Internal
{
    /// <summary>
    /// Transport-neutral representation of a received message. Each <see cref="ITransport"/>
    /// adapts its native delivery type (e.g. RabbitMQ's BasicDeliverEventArgs) into this shape
    /// at the consume boundary so that <see cref="MessageHelper"/> and message consumers never
    /// reference a transport-specific type.
    /// </summary>
    internal sealed class ReceivedTransportMessage(string? type, IDictionary<string, object?> headers, byte[] body)
    {
        /// <summary>The message type's full name (RabbitMQ carries this in BasicProperties.Type).</summary>
        public string? Type { get; } = type;
        public IDictionary<string, object?> Headers { get; } = headers;
        public byte[] Body { get; } = body;
    }
}
