using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    public class AllStatePipesProxyStatusEvent(List<StatePipesProxyStatus> statePipesProxyStatusList) : IEvent
    {
        public List<StatePipesProxyStatus> StatePipesProxyStatusList { get; } = statePipesProxyStatusList;
    }
}
