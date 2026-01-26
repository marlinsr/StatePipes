using RabbitMQ.Client.Events;
using StatePipes.Common;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;
using StatePipes.Messages;
using System.Reflection;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Comms.Internal
{
    internal class StatePipesProxyInternal : IStatePipesProxyInternal, IDisposable
    {
        private readonly Guid _id = Guid.NewGuid();
        private bool _disposedValue;
        private readonly BusConfig _busConfig;
        private readonly EventSubscriptionManager _eventSubscriptionManager = new();
        private readonly EventSubscriptionManager _routingKeyManager = new();
        private readonly TypeDictionary _subscribedEventTypeDictionary = new();
        private readonly HeartbeatEventProcessor _heartbeatProcessor = new();
        private ConnectionChannel? _connectionChannel;
        private readonly string? _hashedPassword;
        public BusConfig BusConfig { get => JsonUtility.Clone(_busConfig); }
        public string Name { get; private set; } = string.Empty;
        public bool IsConnectedToBroker => _connectionChannel?.IsOpen ?? false;
        public bool IsConnectedToService => _heartbeatProcessor.IsConnectedToService;
        public StatePipesProxyInternal(string name, BusConfig busConfig, string? hashedPassword = null)
        {
            Name = name;
            _busConfig = busConfig;
            _hashedPassword = hashedPassword;
            SubscribeConnectedToService(ConnectedToServiceHandler, DisConnectedToServiceHandler);
            _eventSubscriptionManager.Subscribe<HeartbeatEvent>(_heartbeatProcessor.HeartbeatEventHandler);
            _routingKeyManager.Subscribe<HeartbeatEvent>(_heartbeatProcessor.HeartbeatEventHandler);
        }
        public void SubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected) => _heartbeatProcessor.Subscribe(onConnected, onDisconnected);
        public void UnSubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected) => _heartbeatProcessor.UnSubscribe(onConnected, onDisconnected);
        public void Subscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent => Subscribe<TEvent>(typeof(TEvent).FullName, handler);
        public void Subscribe<TEvent>(string? receivedEventTypeFullName, Action<TEvent, BusConfig, bool> handler) where TEvent : class
        {
            if (string.IsNullOrEmpty(receivedEventTypeFullName)) return;
            var eventType = typeof(TEvent);
            _subscribedEventTypeDictionary.Add(receivedEventTypeFullName, eventType);
            var eventTypeFullName = eventType.FullName;
            if (!_routingKeyManager.AlreadyHasSubscriptionForType(receivedEventTypeFullName)) _connectionChannel?.Subscribe(_id, receivedEventTypeFullName, _busConfig);
            _eventSubscriptionManager.Subscribe(eventTypeFullName, handler);
            _routingKeyManager.Subscribe(receivedEventTypeFullName, handler);
        }
        public void UnSubscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent => UnSubscribe<TEvent>(typeof(TEvent).FullName, handler);
        public void UnSubscribe<TEvent>(string? receivedEventTypeFullName, Action<TEvent, BusConfig, bool> handler) where TEvent : class
        {
            if (string.IsNullOrEmpty(receivedEventTypeFullName)) return;
            var eventTypeFullName = typeof(TEvent).FullName;
            Action<HeartbeatEvent, BusConfig, bool> heartbeatHandler = _heartbeatProcessor.HeartbeatEventHandler;
            if (heartbeatHandler.Equals(handler) && eventTypeFullName == typeof(HeartbeatEvent).FullName) return;
            _eventSubscriptionManager.UnSubscribe(eventTypeFullName, handler);
            _routingKeyManager.UnSubscribe(receivedEventTypeFullName, handler);
            if (!_routingKeyManager.AlreadyHasSubscriptionForType(receivedEventTypeFullName)) _connectionChannel?.UnSubscribe(_id, receivedEventTypeFullName, _busConfig);
        }
        public void SendCommand<TCommand>(TCommand command) where TCommand : class, ICommand => SendCommand(typeof(TCommand).FullName, command);
        public void SendCommand<TCommand>(string? sendCommandTypeFullName, TCommand command) where TCommand : class
        {
            if (string.IsNullOrEmpty(sendCommandTypeFullName)) return;
            try
            {
                if (_connectionChannel != null && _connectionChannel.IsOpen)
                {
                    _connectionChannel.Send(sendCommandTypeFullName, command, _busConfig, _busConfig.CommandExchangeName);
                }
                else
                {
                    Log?.LogVerbose($"Failed to send {command.GetType().FullName}");
                }
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        public void SendMessage<TMessage>(TMessage message) where TMessage : class, IMessage
        {
            if (message is ICommand command) SendCommand(command);
        }
        public void Start()
        {
            if (_connectionChannel != null) return;
            _heartbeatProcessor.ResetHeartbeat();
            _connectionChannel = new ConnectionChannel(_busConfig, _hashedPassword, ConfigureBuses);
        }
        public void Stop()
        {
            try
            {
                _heartbeatProcessor.ResetHeartbeat();
                _connectionChannel?.Dispose();
                _connectionChannel = null;
            }
            catch { }
        }
        private void ConfigureBuses(ConnectionChannel connectionChannel)
        {
            try
            {
                var eventSubscription = _routingKeyManager.GetAllSubscriptionTypeFullNames();
                if (eventSubscription.Count <= 0) eventSubscription = ConnectionChannel.DefaultRoutingKeys;
                connectionChannel.ConfigureBus(_id, CommunicationsType.Event, _busConfig.EventExchangeName, ConsumeEvent, eventSubscription);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Response, _busConfig.ResponseExchangeName, ConsumeResponse, ConnectionChannel.DefaultRoutingKeys, true);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Command, _busConfig.CommandExchangeName);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        private Task ConsumeEvent(object model, BasicDeliverEventArgs ea)
        {
            try
            {
                MessageHelper.Deserialize(ea, out object? eventMessage, out BusConfig? busConfig, _subscribedEventTypeDictionary);
                if (eventMessage == null || busConfig == null) return Task.CompletedTask;
                var handleEventMethod = _eventSubscriptionManager.GetType().GetMethod(nameof(EventSubscriptionManager.HandleEvent), BindingFlags.NonPublic | BindingFlags.Instance);
                var handleEventMethodOfEventType = handleEventMethod?.MakeGenericMethod([eventMessage.GetType()]);
                handleEventMethodOfEventType?.Invoke(_eventSubscriptionManager, [eventMessage, busConfig]);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
            return Task.CompletedTask;
        }
        private Task ConsumeResponse(object model, BasicDeliverEventArgs ea)
        {
            try
            {
                MessageHelper.Deserialize(ea, out object? eventMessage, out BusConfig? busConfig, _subscribedEventTypeDictionary);
                if (eventMessage == null || busConfig == null) return Task.CompletedTask;
                var handleEventMethod = _eventSubscriptionManager.GetType().GetMethod(nameof(EventSubscriptionManager.HandleEventResponse), BindingFlags.NonPublic | BindingFlags.Instance);
                var handleEventMethodOfEventType = handleEventMethod?.MakeGenericMethod([eventMessage.GetType()]);
                handleEventMethodOfEventType?.Invoke(_eventSubscriptionManager, [eventMessage, busConfig]);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
            return Task.CompletedTask;
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    Stop();
                }
                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                _disposedValue = true;
            }
        }
        private void ConnectedToServiceHandler(object? sender, EventArgs e) => SendCommand(new GetCurrentStatusCommand());
        private void DisConnectedToServiceHandler(object? sender, EventArgs e) { }
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
