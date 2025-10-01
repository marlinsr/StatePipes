using StatePipes.Comms;

namespace StatePipes.Interfaces
{
    public interface IStatePipesService : IMessageSender
    {
        bool IsConnectedToBroker { get; }
        void SendCommand<TCommand>(TCommand command) where TCommand : class, ICommand;
        void SendCommand<TCommand>(TCommand command, BusConfig? busConfig) where TCommand : class, ICommand;
        void PublishEvent<TEvent>(TEvent eventMessage) where TEvent : class, IEvent;
        void SendResponse<TEvent>(TEvent replyMessage, BusConfig busConfig) where TEvent : class, IEvent;
        void Start();
        void Stop();

    }
}
