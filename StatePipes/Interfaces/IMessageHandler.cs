using StatePipes.Comms;

namespace StatePipes.Interfaces
{
    public interface IMessageHandler<TMessage> where TMessage : class
    {
        void HandleMessage(TMessage message, BusConfig? responseInfo, bool isResponse);
    }
}
