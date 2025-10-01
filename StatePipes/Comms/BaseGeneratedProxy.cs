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
        protected void SendToAll<TCommand>(TCommand command) where TCommand : class, ICommand => _proxyDictionary.Values.ToList().ForEach(proxy => proxy.SendCommand(command));
        protected void Send<TCommand>(string proxyName, TCommand command) where TCommand : class, ICommand
        {
            if (string.IsNullOrEmpty(proxyName))
            {
                SendToAll(command);
                return;
            }
            if (!_proxyDictionary.ContainsKey(proxyName)) return;
            _proxyDictionary[proxyName].SendCommand(command);
        }
        public static T? Convert<T>(object obj)
            where T : class
        {
            var json = JsonUtility.GetJsonStringForObject(obj);
            return JsonUtility.GetObjectForJsonString<T>(json);
        }
        protected void ConvertAndSend<TCommand>(string proxyName, object obj) where TCommand : class, ICommand
        {
            var cmd = Convert<TCommand>(obj);
            if (cmd == null) return;
            Send(proxyName, cmd);
        }
    }
}

