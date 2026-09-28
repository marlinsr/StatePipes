using StatePipes.Interfaces;

namespace StatePipes.Comms.Internal
{
    /// <summary>
    /// Transport-neutral abstraction over the message broker. RabbitMQ-specific types
    /// (channels, exchanges, BasicDeliverEventArgs) live behind implementations such as
    /// <see cref="RabbitMqTransport"/>; consumers depend only on this interface and
    /// <see cref="ReceivedTransportMessage"/>.
    /// </summary>
    internal interface ITransport : IDisposable
    {
        static List<string> DefaultRoutingKeys { get; } = ["#"];
        bool IsOpen { get; }
        void ConfigureBus(Guid id, CommunicationsType commsType, string exchangeName, Func<ReceivedTransportMessage, Task>? consumeMethod = null, List<string>? routingKeys = null, bool autoDelete = false);
        void Subscribe(Guid id, string routingKey, BusConfig busConfig);
        void UnSubscribe(Guid id, string routingKey, BusConfig busConfig);
        void Send<T>(T message, BusConfig busConfigFrom, string exchangeName) where T : IMessage;
        void Send<T>(string? sendCommandTypeFullName, T message, BusConfig busConfigFrom, string exchangeName);
    }
}
