using StatePipes.Interfaces;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Comms.Internal
{
    internal class EventSubscriptionManager
    {
        private readonly Dictionary<string, List<IEventSubscriptionAction>> _subscriptions = [];
        public void Subscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent => Subscribe(typeof(TEvent).FullName, handler);
        public void Subscribe<TEvent>(string? receivedEventTypeFullName, Action<TEvent, BusConfig, bool> handler) where TEvent : class
        {
            if(string.IsNullOrEmpty(receivedEventTypeFullName)) return;
            Log?.LogVerbose($"Subscribing to Event {receivedEventTypeFullName}");
            lock (_subscriptions)
            {
                if (_subscriptions.TryGetValue(receivedEventTypeFullName, out List<IEventSubscriptionAction>? value)) value.Add(new EventSubscriptionAction<TEvent>(handler));
                else _subscriptions.Add(receivedEventTypeFullName, [new EventSubscriptionAction<TEvent>(handler)]);
            }
        }
        public void UnSubscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent => UnSubscribe<TEvent>(typeof(TEvent).FullName, handler);
        public void UnSubscribe<TEvent>(string? receivedEventTypeFullName, Action<TEvent, BusConfig, bool> handler) where TEvent : class
        {
            if (string.IsNullOrEmpty(receivedEventTypeFullName)) return;
            Log?.LogVerbose($"UnSubscribing to Event {receivedEventTypeFullName}");
            lock (_subscriptions)
            {
                if (_subscriptions.TryGetValue(receivedEventTypeFullName, out List<IEventSubscriptionAction>? value))
                {
                    value.RemoveAll(s => s is EventSubscriptionAction<TEvent> esa && esa.Handler == handler);
                    if (_subscriptions[receivedEventTypeFullName].Count == 0) _subscriptions.Remove(receivedEventTypeFullName);
                }
            }
        }
        public bool AlreadyHasSubscriptionForType(Type type) => AlreadyHasSubscriptionForType(type.FullName);
        public bool AlreadyHasSubscriptionForType(string? typeFullName)
        {
            lock (_subscriptions)
            {
                if (string.IsNullOrEmpty(typeFullName)) return false;
                return _subscriptions.ContainsKey(typeFullName);
            }
        }
        public List<string> GetAllSubscriptionTypeFullNames()
        {
            lock( _subscriptions)
            {
                return [.. _subscriptions.Keys];
            }
        }
        internal void HandleEvent<TEvent>(TEvent eventMessage, BusConfig busConfigFrom) where TEvent : class
        {
            lock (_subscriptions)
            {
                var eventTypeFullName = eventMessage.GetType().FullName;
                if (string.IsNullOrEmpty(eventTypeFullName)) return;
                if (_subscriptions.TryGetValue(eventTypeFullName, out List<IEventSubscriptionAction>? value))
                    value.ForEach(h =>
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
        internal void HandleEventResponse<TEvent>(TEvent eventMessage, BusConfig busConfigFrom) where TEvent : class
        {
            lock (_subscriptions)
            {
                var eventTypeFullName = eventMessage.GetType().FullName;
                if (string.IsNullOrEmpty(eventTypeFullName)) return;
                if (_subscriptions.TryGetValue(eventTypeFullName, out List<IEventSubscriptionAction>? value))
                    value.ForEach(h =>
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
