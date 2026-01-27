using Autofac;
using Newtonsoft.Json;
using RabbitMQ.Client.Events;
using StatePipes.Common;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;
using StatePipes.Messages;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Comms.Internal
{
    internal class StatePipesService(string name, ServiceConfiguration serviceConfiguration, IStatePipesProxyFactory? parentProxyFactory = null, bool remoteAccess = true) : TaskWrapper<ReceivedCommandMessage>, IStatePipesService, IStatePipesProxyInternal, IDisposable
    {
        private readonly LocalProxyMessageTypeTransformer _localProxyMessageTypeTransformer = new();
        private readonly TypeDictionary _externalMessageTypeDictionary = new();
        private readonly Guid _id = Guid.NewGuid();
        private readonly EventSubscriptionManager _eventSubscriptionManager = new();
        private DelayedMessageSender<HeartbeatCommand>? _heartbeatSender;
        private IContainer? _container;
        private ConnectionChannel? _connectionChannel;
        private List<string> PublicCommandsFullName = ConnectionChannel.DefaultRoutingKeys;
#pragma warning disable IDE1006 // Naming Styles
        private BusConfig _busConfig => serviceConfiguration.BusConfig;
#pragma warning restore IDE1006 // Naming Styles
        public BusConfig BusConfig { get => JsonUtility.Clone(_busConfig); }
        public string Name { get; private set; } = name;
        public bool IsConnectedToBroker => remoteAccess && (_connectionChannel?.IsOpen ?? false);
        public bool IsConnectedToService => true;
        public StatePipesService(ServiceConfiguration serviceConfiguration) : this(string.Empty, serviceConfiguration) { }
        public void SubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected) => onConnected.Invoke(null, EventArgs.Empty);
        public void UnSubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected) { }
        public void SendCommand<TCommand>(TCommand command) where TCommand : class, ICommand => SendCommand(command, _busConfig);
        public void SendCommand<TCommand>(string? sendCommandTypeFullName, TCommand command) where TCommand : class
        {
            if (string.IsNullOrEmpty(sendCommandTypeFullName)) return;
            dynamic? transformedCommand = _localProxyMessageTypeTransformer.TransformValueObjectToCommand(sendCommandTypeFullName, command);
            if (transformedCommand == null) return;
            SendCommand(transformedCommand);
        }
        public void SendCommand<TCommand>(TCommand command, BusConfig? busConfig) where TCommand : class, ICommand
        {
            if (_container != null) Queue(new ReceivedCommandMessage(command, busConfig ?? _busConfig));
        }
        private void EventSendHelper<TEvent>(TEvent eventMessage, string exchangeName, BusConfig busConfig) where TEvent : class, IEvent
        {
            try
            {
                if (_connectionChannel == null || !eventMessage.GetType().IsPublic) return;
                if (_connectionChannel.IsOpen) _connectionChannel.Send(eventMessage, busConfig, exchangeName);
                else Log?.LogVerbose($"Failed to send {eventMessage.GetType().FullName}");
            }
            catch (Exception ex) { Log?.LogException(ex); }
        }
        public void PublishEvent<TEvent>(TEvent eventMessage) where TEvent : class, IEvent
        {
            EventSendHelper(eventMessage, _busConfig.EventExchangeName, _busConfig);
            _eventSubscriptionManager.HandleEvent(eventMessage, _busConfig);
            var transformedEvent = _localProxyMessageTypeTransformer.TransformEventToValueObject(eventMessage);
            if (transformedEvent != null) _eventSubscriptionManager.HandleEvent(transformedEvent, _busConfig);
            if (_container != null) ExecuteMessageHelper.ExecuteMessage(eventMessage, _busConfig, false, _container);
        }
        public void SendResponse<TEvent>(TEvent replyMessage, BusConfig busConfig) where TEvent : class, IEvent
        {
            if (busConfig.BrokerUri != _busConfig.BrokerUri)
            {
                Log?.LogError($"Can't send response {replyMessage.GetType().FullName} because it is on broker {busConfig.BrokerUri}");
                return;
            }
            if(busConfig.ClientCertPath != _busConfig.ClientCertPath)
            {
                Log?.LogError($"Can't send response {replyMessage.GetType().FullName} because it uses a {busConfig.ClientCertPath} certificate for authentication");
                return;
            }
            Log?.LogVerbose($"Sending response {typeof(TEvent).FullName} to {busConfig.ResponseExchangeName}");
            EventSendHelper(replyMessage, busConfig.ResponseExchangeName, busConfig);
            _eventSubscriptionManager.HandleEventResponse(replyMessage, _busConfig);
            var transformedEvent = _localProxyMessageTypeTransformer.TransformEventToValueObject(replyMessage);
            if (transformedEvent != null) _eventSubscriptionManager.HandleEventResponse(transformedEvent, _busConfig);
        }
        public void SendMessage<TMessage>(TMessage message) where TMessage : class, IMessage
        {
            if (message is ICommand command) SendCommand((dynamic)command);
            if (message is IEvent ev) PublishEvent((dynamic)ev);
        }
        public void Start()
        {
            if (_container != null) return;
            ContainerBuilder containerBuilder = new();
            var statePipesServiceContainerSetup = new StatePipesServiceContainerSetup(serviceConfiguration, parentProxyFactory);
            _localProxyMessageTypeTransformer.PopulatePublicCommandTypeDictionary(statePipesServiceContainerSetup.ClassLibraryAssembly);
            _externalMessageTypeDictionary.SetupAssembylyMessageTypes(statePipesServiceContainerSetup.ClassLibraryAssembly);
            statePipesServiceContainerSetup.Register(containerBuilder);
            containerBuilder.Register(c => this).As<IStatePipesService>().SingleInstance();
            var container = containerBuilder.Build();
            _container = container;
            statePipesServiceContainerSetup.Build(container);
            PublicCommandsFullName = statePipesServiceContainerSetup.PublicCommandsFullName;
            StartLongRunningAndWait();
            _heartbeatSender = new DelayedMessageSender<HeartbeatCommand>(this);
            _heartbeatSender.StartPeriodic(TimeSpan.FromMilliseconds(StatePipesConnectionFactory.HeartbeatIntervalMilliseconds), new HeartbeatCommand());
            SendCommand(new GetCurrentStatusCommand());
        }
        public void Stop()
        {
            try
            {
                _heartbeatSender?.Stop();
                _heartbeatSender = null;
                Cancel();
                _connectionChannel?.Dispose();
                _connectionChannel = null;
                _container?.Dispose();
                _container = null;
            }
            catch (Exception ex) 
            { 
                Log?.LogException(ex);
            }
        }
        private Task ConsumeCommand(object model, BasicDeliverEventArgs ea)
        {
            try
            {
                if (_container == null) return Task.CompletedTask;
                MessageHelper.Deserialize(ea, out object? command, out BusConfig? busConfig, _externalMessageTypeDictionary);
                if (command == null || busConfig == null) return Task.CompletedTask;
                Queue(new ReceivedCommandMessage((object)command, busConfig));
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
                if (_container == null) return Task.CompletedTask;
                MessageHelper.Deserialize(ea, out object? eventMessage, out BusConfig? busConfig, _externalMessageTypeDictionary);
                if (eventMessage == null || busConfig == null) return Task.CompletedTask;
                ExecuteMessageHelper.ExecuteMessage(eventMessage, busConfig, true, _container);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
            return Task.CompletedTask;
        }
        private void ConfigureBuses(ConnectionChannel connectionChannel)
        {
            try
            {
                connectionChannel.ConfigureBus(_id, CommunicationsType.Command, _busConfig.CommandExchangeName, ConsumeCommand, PublicCommandsFullName);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Response, _busConfig.ResponseExchangeName, ConsumeResponse, ConnectionChannel.DefaultRoutingKeys, true);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Event, _busConfig.EventExchangeName);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        protected override void DoWork()
        {
            while (true)
            {
                PerformCancellation();
                if(_connectionChannel == null && remoteAccess) try { _connectionChannel = new ConnectionChannel(_busConfig, null, ConfigureBuses); } catch { };
                if (_container != null)
                {
                    var cmd = WaitGetNext(StatePipesConnectionFactory.HeartbeatIntervalMilliseconds);
                    PerformCancellation();
                    if (cmd != null) ExecuteMessageHelper.ExecuteMessage(cmd.Command, cmd.ReplyTo, false, _container);
                }
                else Thread.Sleep(StatePipesConnectionFactory.HeartbeatIntervalMilliseconds);
            }
        }
        public override void Dispose()
        {
            Stop();
            base.Dispose();
        }
        public void Subscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent => Subscribe(typeof(TEvent).FullName, handler);
        public void Subscribe<TEvent>(string? receivedEventTypeFullName, Action<TEvent, BusConfig, bool> handler) where TEvent : class
        {
            if (string.IsNullOrEmpty(receivedEventTypeFullName)) return;
            var eventType = typeof(TEvent);
            var eventTypeFullName = eventType.FullName;
            if (eventTypeFullName != receivedEventTypeFullName) _localProxyMessageTypeTransformer.AddEventType(receivedEventTypeFullName, eventType);
            _eventSubscriptionManager.Subscribe(eventTypeFullName, handler);
        }
        public void UnSubscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent => _eventSubscriptionManager.UnSubscribe(handler);
    }
}
