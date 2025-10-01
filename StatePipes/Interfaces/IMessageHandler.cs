using StatePipes.Comms;

namespace StatePipes.Interfaces
{
    public interface IMessageHandler<TMessage> where TMessage : IMessage
    {
        void HandleMessage(TMessage message, BusConfig? responseInfo, bool isResponse);
    }
}
