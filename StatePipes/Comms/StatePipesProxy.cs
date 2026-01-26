using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using StatePipes.Messages;
namespace StatePipes.Comms
{
    public class StatePipesProxy : IStatePipesProxy, IDisposable
    {
        private bool _disposedValue;
        private readonly IStatePipesProxyInternal _proxy;
        public BusConfig BusConfig {  get => _proxy.BusConfig; }
        public string Name { get => _proxy.Name;}
        public bool IsConnectedToBroker => _proxy.IsConnectedToBroker;
        public bool IsConnectedToService => _proxy.IsConnectedToService;
        public StatePipesProxy(string name, BusConfig busConfig, string? hashedPassword = null)
        {
            _proxy = new StatePipesProxyInternal(name, busConfig, hashedPassword);
        }
        public void SubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected) => _proxy.SubscribeConnectedToService(onConnected, onDisconnected);
        public void UnSubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected) => _proxy.UnSubscribeConnectedToService(onConnected, onDisconnected);
        public void Subscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent => _proxy.Subscribe<TEvent>(typeof(TEvent).FullName, handler);
        public void UnSubscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent => _proxy.UnSubscribe<TEvent>(handler);
        public void SendCommand<TCommand>(TCommand command) where TCommand : class, ICommand => _proxy.SendCommand(command);
        public void SendMessage<TMessage>(TMessage message) where TMessage : class, IMessage => _proxy.SendMessage(message);
        public void Start() => _proxy.Start();
        public void Stop() => _proxy.Stop();
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    _proxy.Dispose();
                }
                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                _disposedValue = true;
            }
        }
        private void ConnectedToServiceHandler(object? sender, EventArgs e) => SendCommand(new GetCurrentStatusCommand());
        private void DisConnectedToServiceHandler(object? sender, EventArgs e) {}
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
