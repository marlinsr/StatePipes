using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    public class HeartbeatEvent(long counter) : IEvent
    {
        public long Counter { get; } = counter;
    }
}
