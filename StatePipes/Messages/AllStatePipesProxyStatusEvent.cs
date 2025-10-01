using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    public class AllStatePipesProxyStatusEvent : IEvent
    {
        public List<StatePipesProxyStatus> StatePipesProxyStatusList { get; }
        public AllStatePipesProxyStatusEvent(List<StatePipesProxyStatus> statePipesProxyStatusList)
        {
            StatePipesProxyStatusList = statePipesProxyStatusList;
        }
    }
}
