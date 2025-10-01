using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.Comms.Internal
{
    internal class GetAllStatePipesProxyStatusCommandHandler : IMessageHandler<GetAllStatePipesProxyStatusCommand>
    {
        private readonly List<IStatePipesProxy> _proxies;
        private readonly IStatePipesProxyFactory _proxyFactory;
        private readonly IStatePipesService _bus;
        public GetAllStatePipesProxyStatusCommandHandler(IStatePipesProxyFactory proxyFactory, IStatePipesService bus)
        {
            _proxyFactory = proxyFactory;
            _proxies = _proxyFactory.GetAllClientProxies();
            _bus = bus;
        }
        public void HandleMessage(GetAllStatePipesProxyStatusCommand message, BusConfig? responseInfo, bool isResponse)
        {
            if (responseInfo == null) return;
            List<StatePipesProxyStatus> clientComms = [];
            _proxies.ForEach(StatePipesProxy => clientComms.Add(new StatePipesProxyStatus(StatePipesProxy.Name, StatePipesProxy.IsConnectedToService, StatePipesProxy.IsConnectedToBroker)));
            _bus.SendResponse(new AllStatePipesProxyStatusEvent(clientComms), responseInfo);
        }
    }
}
