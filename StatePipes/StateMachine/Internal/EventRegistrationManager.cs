namespace StatePipes.StateMachine.Internal
{
    internal class EventRegistrationManager
    {
        private readonly Dictionary<string, List<String>> _eventRegistrations = [];
        public void RegisterEvent(Type ev, string state)
        {
            var eventName = ev.Name;
            if (string.IsNullOrEmpty(state)) return;
            if (!_eventRegistrations.ContainsKey(state)) _eventRegistrations.Add(state, []);
            if(!_eventRegistrations[state].Contains(eventName)) _eventRegistrations[state].Add(eventName);
        }
        public IReadOnlyList<string> GetRegisteredEvents(string state)
        {
            if(!_eventRegistrations.TryGetValue(state, out List<string>? value)) return [];
            return value;
        }
    }
}
