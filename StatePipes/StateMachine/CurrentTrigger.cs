using StatePipes.Comms;
using StatePipes.Interfaces;

namespace StatePipes.StateMachine
{
    public class CurrentTrigger(ITrigger trigger, BusConfig? responseInfo)
    {
        public ITrigger Trigger { get; } = trigger;
        public BusConfig? ResponseInfo { get; } = responseInfo;
    }
}
