using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    public class AllStateStatusEvent(IReadOnlyList<StateStatus> stateStatuses) : IEvent
    {
        public IReadOnlyList<StateStatus> StateStatuses { get; } = stateStatuses;
    }
}
