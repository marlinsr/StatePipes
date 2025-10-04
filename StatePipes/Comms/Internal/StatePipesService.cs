using Autofac;
using RabbitMQ.Client.Events;
using StatePipes.Common;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;
using StatePipes.Messages;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Comms.Internal
{
    internal class StatePipesService : TaskWrapper<ReceivedCommandMessage>, IStatePipesService, IStatePipesProxy, IDisposable
    {
        private readonly TypeDictionary _externalMessageTypeDictionary = new TypeDictionary();
        private readonly Guid _id = Guid.NewGuid();
        private readonly IStatePipesProxyFactory? _parentProxyFactory;
        private readonly ServiceConfiguration _serviceConfiguration;
        private readonly EventSubscriptionManager _eventSubscriptionManager = new();
        private DelayedMessageSender<HeartbeatCommand>? _heartbeatSender;
        private IContainer? _container;
        private ConnectionChannel? _connectionChannel;
        private List<string> PublicCommandsFullName = ConnectionChannel.DefaultRoutingKeys;
        private BusConfig _busConfig => _serviceConfiguration.BusConfig;
        public BusConfig BusConfig { get => JsonUtility.Clone(_busConfig); }
        public string Name { get; private set; } = string.Empty;
        public bool IsConnectedToBroker => _remoteAccess && (_connectionChannel?.IsOpen ?? false);
        public bool IsConnectedToService => true;
        private readonly bool _remoteAccess;
        public StatePipesService(ServiceConfiguration serviceConfiguration) : this(string.Empty, serviceConfiguration) { }
        public StatePipesService(string name, ServiceConfiguration serviceConfiguration, IStatePipesProxyFactory? parentProxyFactory = null, bool remoteAccess = true)
        {
            Name = name;
            _serviceConfiguration = serviceConfiguration;
            _parentProxyFactory = parentProxyFactory;
            _remoteAccess = remoteAccess;
        }
        public void SubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected) => onConnected.Invoke(null, EventArgs.Empty);
        public void UnSubscribeConnectedToService(EventHandler onConnected, EventHandler onDisconnected) { }
        public void SendCommand<TCommand>(TCommand command) where TCommand : class, ICommand => SendCommand(command, _busConfig);
        public void SendCommand<TCommand>(TCommand command, BusConfig? busConfig) where TCommand : class, ICommand
        {
            if (_container != null) Queue(new ReceivedCommandMessage(command, busConfig == null ? _busConfig : busConfig));
        }
        private void EventSendHelper<TEvent>(TEvent eventMessage, string exchangeName) where TEvent : class, IEvent
        {
            try
            {
                if (_connectionChannel != null && eventMessage.GetType().IsPublic)
                {
                    if (_connectionChannel.IsOpen)
                    {
                        _connectionChannel.Send(eventMessage, _busConfig, exchangeName);
                    }
                    else
                    {
                        Log?.LogVerbose($"Failed to send {eventMessage.GetType().FullName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        public void PublishEvent<TEvent>(TEvent eventMessage) where TEvent : class, IEvent
        {
            Log?.LogVerbose($"Publishing {typeof(TEvent).FullName}");
            EventSendHelper(eventMessage, _busConfig.EventExchangeName);
            _eventSubscriptionManager.HandleEvent(eventMessage, _busConfig);
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
            EventSendHelper(replyMessage, busConfig.ResponseExchangeName);
            _eventSubscriptionManager.HandleEventResponse(replyMessage, _busConfig);
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
            var statePipesServiceContainerSetup = new StatePipesServiceContainerSetup(_serviceConfiguration, _parentProxyFactory);
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
                MessageHelper.Deserialize(ea, out IMessage? command, out BusConfig? busConfig, _externalMessageTypeDictionary);
                if (command == null || busConfig == null) return Task.CompletedTask;
                Queue(new ReceivedCommandMessage((ICommand)command, busConfig));
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
                MessageHelper.Deserialize(ea, out IMessage? eventMessage, out BusConfig? busConfig, _externalMessageTypeDictionary);
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
                if(_connectionChannel == null && _remoteAccess)
                {
                    try { _connectionChannel = new ConnectionChannel(_busConfig, null, ConfigureBuses); } catch { };
                }
                if (_container != null)
                {
                    var cmd = WaitGetNext(StatePipesConnectionFactory.HeartbeatIntervalMilliseconds);
                    PerformCancellation();
                    if (cmd != null) ExecuteMessageHelper.ExecuteMessage(cmd.Command, cmd.ReplyTo, false, _container);
                }
                else
                {
                    Thread.Sleep(StatePipesConnectionFactory.HeartbeatIntervalMilliseconds);
                }
            }
        }
        public override void Dispose()
        {
            Stop();
            base.Dispose();
        }
        public void Subscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent => _eventSubscriptionManager.Subscribe(handler);
        public void UnSubscribe<TEvent>(Action<TEvent, BusConfig, bool> handler) where TEvent : class, IEvent => _eventSubscriptionManager.UnSubscribe(handler);
    }
}
