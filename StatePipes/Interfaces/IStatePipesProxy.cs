using StatePipes.Comms;

namespace StatePipes.Interfaces
{
    public interface IStatePipesProxy : IMessageSender, IDisposable
    {
        BusConfig BusConfig { get; }
        public string Name { get; }
        public bool IsConnectedToService { get; }
        public bool IsConnectedToBroker { get; }
        void SubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected);
        void UnSubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected);
        void Subscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent;
        void UnSubscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent;
        void SendCommand<TCommand>(TCommand command) where TCommand : class, ICommand;
        void Start();
        void Stop();
    }
}
