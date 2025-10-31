using StatePipes.Interfaces;

namespace StatePipes.StateMachine.Internal
{
    internal class EventRegistrationManager
    {
        private readonly Dictionary<string, List<String>> _eventRegistrations = new();
        public void RegisterEvent<TEvent>(string state) where TEvent : IEvent
        {
            var eventName = typeof(TEvent).Name;
            if (string.IsNullOrEmpty(state)) return;
            if (!_eventRegistrations.ContainsKey(state)) _eventRegistrations.Add(state, new List<string>());
            if(!_eventRegistrations[state].Contains(eventName)) _eventRegistrations[state].Add(eventName);
        }
        public IReadOnlyList<string> GetRegisteredEvents(string state)
        {
            if(!_eventRegistrations.ContainsKey(state)) return new List<string>();
            return _eventRegistrations[state];
        }

        public bool IsEventRegisteredForState<TEvent>(string state) where TEvent : IEvent
        {
            var eventName = typeof(TEvent).Name;
            if (!_eventRegistrations.ContainsKey(state)) return false;
            return _eventRegistrations[state].Contains(eventName);
        }
    }
}
