using StatePipes.Interfaces;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Comms.Internal
{
    internal class EventSubscriptionManager
    {
        private Dictionary<string, List<IEventSubscriptionAction>> _subscriptions = new Dictionary<string, List<IEventSubscriptionAction>>();
        public void Subscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : IEvent
        {
            lock (_subscriptions)
            {
                var eventTypeFullName = typeof(TEvent).FullName;
                if (string.IsNullOrEmpty(eventTypeFullName)) return;
                Log?.LogVerbose($"Subscribing to Event {typeof(TEvent).FullName}");
                if (_subscriptions.ContainsKey(eventTypeFullName))
                {
                    _subscriptions[eventTypeFullName].Add(new EventSubscriptionAction<TEvent>(handler));
                }
                else
                {
                    _subscriptions.Add(eventTypeFullName, [new EventSubscriptionAction<TEvent>(handler)]);
                }
            }
        }
        public void UnSubscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : IEvent
        {
            lock (_subscriptions)
            {
                var eventTypeFullName = typeof(TEvent).FullName;
                if (string.IsNullOrEmpty(eventTypeFullName)) return;
                Log?.LogVerbose($"UnSubscribing to Event {typeof(TEvent).FullName}");
                if (_subscriptions.ContainsKey(eventTypeFullName))
                {
                    _subscriptions[eventTypeFullName].RemoveAll(s => s is EventSubscriptionAction<TEvent> esa && esa.Handler == handler );
                    if (_subscriptions[eventTypeFullName].Count == 0)
                    {
                        _subscriptions.Remove(eventTypeFullName);
                    }
                }
            }
        }
        public bool AlreadyHasSubscriptionForType(Type type)
        {
            lock (_subscriptions)
            {
                var eventTypeFullName = type.FullName;
                if (string.IsNullOrEmpty(eventTypeFullName)) return false;
                return _subscriptions.ContainsKey(eventTypeFullName);
            }
        }
        public List<string> GetAllSubscriptionTypeFullNames()
        {
            lock( _subscriptions)
            {
                return _subscriptions.Keys.ToList();
            }
        }
        internal void HandleEvent<TEvent>(TEvent eventMessage, BusConfig busConfigFrom) where TEvent : class, IEvent
        {
            lock (_subscriptions)
            {
                var eventTypeFullName = eventMessage.GetType().FullName;
                if (string.IsNullOrEmpty(eventTypeFullName)) return;
                if (_subscriptions.ContainsKey(eventTypeFullName))
                    _subscriptions[eventTypeFullName].ForEach(h =>
                    {
                        try
                        {
                            Log?.LogVerbose($"HandleEvent: Executing Handler for {eventTypeFullName}");
                            ((EventSubscriptionAction<TEvent>)h).Handler(eventMessage, busConfigFrom, false);
                        }
                        catch (Exception ex)
                        {
                            Log?.LogException(ex);
                        }
                    });
            }
        }
        internal void HandleEventResponse<TEvent>(TEvent eventMessage, BusConfig busConfigFrom) where TEvent : IEvent
        {
            lock (_subscriptions)
            {
                var eventTypeFullName = eventMessage.GetType().FullName;
                if (string.IsNullOrEmpty(eventTypeFullName)) return;
                if (_subscriptions.ContainsKey(eventTypeFullName))
                    _subscriptions[eventTypeFullName].ForEach(h =>
                    {
                        try
                        {
                            Log?.LogVerbose($"HandleEventResponse: Executing Handler for {eventTypeFullName}");

                            ((EventSubscriptionAction<TEvent>)h).Handler(eventMessage, busConfigFrom, true);
                        }
                        catch (Exception ex)
                        {
                            Log?.LogException(ex);
                        }
                    });
            }
        }
    }
}
