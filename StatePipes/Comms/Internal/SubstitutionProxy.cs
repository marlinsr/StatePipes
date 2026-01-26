using StatePipes.Interfaces;

namespace StatePipes.Comms.Internal
{
    internal class SubstitutionProxy(string name, IStatePipesProxyInternal innerProxy) : IStatePipesProxyInternal
    {
        private readonly string _name = name;
        private readonly IStatePipesProxyInternal _innerProxy = innerProxy;
        public BusConfig BusConfig => _innerProxy.BusConfig;
        public string Name => _name;
        public bool IsConnectedToService => _innerProxy.IsConnectedToService;
        public bool IsConnectedToBroker => _innerProxy.IsConnectedToBroker;
        public void Dispose() => _innerProxy.Dispose();
        public void SendCommand<TCommand>(TCommand command) where TCommand : class, ICommand => _innerProxy.SendCommand(command);
        public void SendCommand<TCommand>(string? sendCommandTypeFullName, TCommand command) where TCommand : class => _innerProxy.SendCommand(sendCommandTypeFullName, command);
        public void SendMessage<TMessage>(TMessage message) where TMessage : class, IMessage => _innerProxy.SendMessage(message);
        public void Start() => _innerProxy.Start();
        public void Stop() => _innerProxy.Stop();
        public void Subscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent => _innerProxy.Subscribe(handler);
        public void Subscribe<TEvent>(string? receivedEventTypeFullName, Action<TEvent, BusConfig, bool> handler) where TEvent : class =>
            _innerProxy.Subscribe(receivedEventTypeFullName, handler);
        public void SubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected) => _innerProxy.SubscribeConnectedToService(onConnected, onDisconnected);
        public void UnSubscribe<TEvent>(Action<TEvent, BusConfig,bool> handler) where TEvent : class, IEvent => _innerProxy.UnSubscribe(handler);
        public void UnSubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected) => _innerProxy.UnSubscribeConnectedToService(onConnected, onDisconnected);
    }
}
