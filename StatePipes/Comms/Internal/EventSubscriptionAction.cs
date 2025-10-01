using StatePipes.Interfaces;

namespace StatePipes.Comms.Internal
{
    internal class EventSubscriptionAction<TEvent>(Action<TEvent, BusConfig, bool> handler) : IEventSubscriptionAction where TEvent : IEvent
    {
        public Action<TEvent, BusConfig, bool> Handler { get; } = handler;
    }
}
