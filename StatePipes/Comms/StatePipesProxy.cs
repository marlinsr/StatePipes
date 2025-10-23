using RabbitMQ.Client.Events;
using StatePipes.Common;
using StatePipes.Common.Internal;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using StatePipes.Messages;
using System.Reflection;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Comms
{
    public class StatePipesProxy : IStatePipesProxy, IDisposable
    {
        private readonly Guid _id = Guid.NewGuid();
        private bool _disposedValue;
        private readonly BusConfig _busConfig;
        private readonly EventSubscriptionManager _eventSubscriptionManager = new();
        private readonly TypeDictionary _subscribedEventTypeDictionary = new TypeDictionary();
        private readonly HeartbeatEventProcessor _heartbeatProcessor = new();
        private ConnectionChannel? _connectionChannel;
        private string? _hashedPassword;
        public BusConfig BusConfig {  get => JsonUtility.Clone(_busConfig); }
        public string Name { get; private set; } = string.Empty;
        public bool IsConnectedToBroker => _connectionChannel?.IsOpen ?? false;
        public bool IsConnectedToService => _heartbeatProcessor.IsConnectedToService;
        public StatePipesProxy(string name, BusConfig busConfig, string? hashedPassword = null)
        {
            Name = name;
            _busConfig = busConfig;
            _hashedPassword = hashedPassword;
            SubscribeConnectedToService(ConnectedToServiceHandler, DisConnectedToServiceHandler);
            _eventSubscriptionManager.Subscribe<HeartbeatEvent>(_heartbeatProcessor.HeartbeatEventHandler);
        }
        public void SubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected)
        {
            _heartbeatProcessor.Subscribe(onConnected, onDisconnected);
        }
        public void UnSubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected)
        {
            _heartbeatProcessor.UnSubscribe(onConnected, onDisconnected);
        }
        public void Subscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent
        {
            var eventType = typeof(TEvent);
            _subscribedEventTypeDictionary.Add(eventType);
            if (string.IsNullOrEmpty(eventType.FullName)) return;
            if (!_eventSubscriptionManager.AlreadyHasSubscriptionForType(eventType)) _connectionChannel?.Subscribe(_id, eventType.FullName, _busConfig);
            _eventSubscriptionManager.Subscribe(handler);
        }
        public void UnSubscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent
        {
            Action<HeartbeatEvent, BusConfig, bool> heartbeatHandler = _heartbeatProcessor.HeartbeatEventHandler;
            if (heartbeatHandler.Equals(handler)) return;
            _eventSubscriptionManager.UnSubscribe(handler);
            var eventType = typeof(TEvent);
            if (string.IsNullOrEmpty(eventType.FullName)) return;
            if (!_eventSubscriptionManager.AlreadyHasSubscriptionForType(eventType)) _connectionChannel?.UnSubscribe(_id, eventType.FullName, _busConfig);
        }
        public void SendCommand<TCommand>(TCommand command) where TCommand : class, ICommand
        {
            try
            {
                if (_connectionChannel != null && _connectionChannel.IsOpen)
                {
                    _connectionChannel.Send(command, _busConfig, _busConfig.CommandExchangeName);
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
            if (message is ICommand) SendCommand((ICommand)message);
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
                var eventSubscription = _eventSubscriptionManager.GetAllSubscriptionTypeFullNames();
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
                MessageHelper.Deserialize(ea, out IMessage? eventMessage, out BusConfig? busConfig, _subscribedEventTypeDictionary);
                if (eventMessage == null || busConfig == null) return Task.CompletedTask;
                var handleEventMethod = _eventSubscriptionManager.GetType().GetMethod(nameof(EventSubscriptionManager.HandleEvent), BindingFlags.NonPublic | BindingFlags.Instance);
                var handleEventMethodOfEventType = handleEventMethod?.MakeGenericMethod(new[] { eventMessage.GetType() });
                handleEventMethodOfEventType?.Invoke(_eventSubscriptionManager, new object[] { eventMessage, busConfig });
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
                MessageHelper.Deserialize(ea, out IMessage? eventMessage, out BusConfig? busConfig, _subscribedEventTypeDictionary);
                if (eventMessage == null || busConfig == null) return Task.CompletedTask;
                var handleEventMethod = _eventSubscriptionManager.GetType().GetMethod(nameof(EventSubscriptionManager.HandleEventResponse), BindingFlags.NonPublic | BindingFlags.Instance);
                var handleEventMethodOfEventType = handleEventMethod?.MakeGenericMethod(new[] { eventMessage.GetType() });
                handleEventMethodOfEventType?.Invoke(_eventSubscriptionManager, new object[] { eventMessage, busConfig });
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
        private void DisConnectedToServiceHandler(object? sender, EventArgs e) {}
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
