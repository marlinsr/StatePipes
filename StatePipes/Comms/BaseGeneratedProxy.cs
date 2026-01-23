using StatePipes.Common;
using StatePipes.Interfaces;

namespace StatePipes.Comms
{
    public abstract class BaseGeneratedProxy
    {
        protected readonly Dictionary<string, IStatePipesProxy> _proxyDictionary = new Dictionary<string, IStatePipesProxy>();
        protected readonly IStatePipesService _bus;
        public abstract string ProxyPrefix { get; }
        protected abstract void Subscribe(IStatePipesProxy proxy);
        protected void ConnectionStatusSetup()
        {
            _proxyDictionary.Values.ToList().ForEach(proxy =>
            {
                proxy.SubscribeConnectedToService(
                    (sender, args) => SendConnectionStatusTrigger(proxy.Name, true),
                    (sender, args) => SendConnectionStatusTrigger(proxy.Name, false)
                );
            });
        }
        protected abstract void SendConnectionStatusTrigger(string proxyName, bool isConnected);
        public BaseGeneratedProxy(IStatePipesProxyFactory proxyFactory, IStatePipesService bus)
        {
            _bus =bus;
            var proxyList = proxyFactory.GetAllClientProxies().Where(p => p.Name.StartsWith(ProxyPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
            proxyList.ForEach(proxy =>
            {
                _proxyDictionary.Add(proxy.Name, proxy);
            });
            SubscribeAll();
            ConnectionStatusSetup();
        }
        public void Start() => StartAll();
        protected void StartAll() => _proxyDictionary.Values.ToList().ForEach(proxy => proxy.Start());
        protected void SubscribeAll() => _proxyDictionary.Values.ToList().ForEach(proxy => Subscribe(proxy));
        protected void SendToAll<TCommand>(string? sendCommandTypeFullName, TCommand command) where TCommand : class => _proxyDictionary.Values.ToList().ForEach(proxy => proxy.SendCommand(sendCommandTypeFullName, command));
        protected void Send<TCommand>(string proxyName, string? sendCommandTypeFullName, TCommand command) where TCommand : class
        {
            if (string.IsNullOrEmpty(proxyName))
            {
                SendToAll(sendCommandTypeFullName, command);
                return;
            }
            if (!_proxyDictionary.ContainsKey(proxyName)) return;
            _proxyDictionary[proxyName].SendCommand(sendCommandTypeFullName, command);
        }
    }
}

