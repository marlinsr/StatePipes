using StatePipes.Comms;
using StatePipes.Messages;
using StatePipes.SelfDescription;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Explorer.NonWebClasses
{
    public class StatePipesHandlerHelper : IDisposable
    {
        private StatePipesProxy? _proxy;
        private readonly EventJsonRepository _eventJosonRepo = new();
        private readonly CommandJsonRepository _cmdExampleJsonRepo = new();
        private Dictionary<string, DynamicEventHandler> _dynamicEventHandlerDictionary = [];
        private Dictionary<string, DynamicCommandSender> _dynamicCommandSenderDictionary = [];
        private System.Threading.Timer? _timer;
        private bool disposedValue;
        private ExcludeAndIncludeLists _filters = new ExcludeAndIncludeLists();
        private readonly TypeSerializationConverter _typeSerializationConverter = new TypeSerializationConverter();
        public void Initialize(string brokerUri, string exchangeName, ExcludeAndIncludeLists filters, string? clientCertFileName, string? clientCertPasswordFileName, string? hashedPassword)
        {
            if (string.IsNullOrEmpty(brokerUri)) return;
            Cleanup();
            _filters = filters.Clone();
            if (string.IsNullOrEmpty(clientCertFileName) || string.IsNullOrEmpty(clientCertPasswordFileName) || string.IsNullOrEmpty(hashedPassword))
            {
                //SRM do insecure ??? Don't support for now
                return;
            }
            else
            {
                var busConfig = new BusConfig(brokerUri, exchangeName, clientCertFileName, clientCertPasswordFileName);
                _proxy = new StatePipesProxy(string.Empty, busConfig, hashedPassword);
                _proxy.SubscribeConnectedToService(OnConnected, OnDisconnected);
                _proxy.Start();
            }
            if( _proxy != null) _timer = new Timer(_ => {_proxy.SendCommand(new GetSelfDescriptionCommand());}, null, 0, 1000);
        }
        private void OnConnected(object? sender, EventArgs e) => 
            _proxy?.Subscribe<SelfDescriptionEvent>(UpdateServiceDescriptionEvent);
        private void OnDisconnected(object? sender, EventArgs e) {}
        public List<EventEntry> EventJsons => _eventJosonRepo.GetEventJsons();
        public List<CommandEntry> CommandList => _cmdExampleJsonRepo.GetCommandList();
        public bool IsConnectedToBroker => _proxy?.IsConnectedToBroker ?? false;
        public void SendCommand(string commandTypeFullName, string commandJson)
        {
            if (!(_proxy?.IsConnectedToService ?? false)) return;
            if (!_dynamicCommandSenderDictionary.ContainsKey(commandTypeFullName))
            {
                Log?.LogError($"_dynamicCommandSenderDictionary {commandTypeFullName} does not exist");
                return;
            }
            _dynamicCommandSenderDictionary[commandTypeFullName].Send(commandJson);
        }
        public object? GetCommandObject(string commandTypeFullName, string commandJson)
        {
            if (!_dynamicCommandSenderDictionary.ContainsKey(commandTypeFullName))
            {
                Log?.LogError($"_dynamicCommandSenderDictionary {commandTypeFullName} does not exist");
                return null;
            }
            return _dynamicCommandSenderDictionary[commandTypeFullName].GetCommandObject(commandJson);
        }
        public dynamic? TypeDefault(string commandTypeFullName, Type t)
        {
            if (!_dynamicCommandSenderDictionary.ContainsKey(commandTypeFullName))
            {
                Log?.LogError($"_dynamicCommandSenderDictionary {commandTypeFullName} does not exist");
                return null;
            }
            return _dynamicCommandSenderDictionary[commandTypeFullName].TypeDefault(t);
        }
        public void ResetJson(string commandTypeFullName) => _cmdExampleJsonRepo.ResetJson(commandTypeFullName);
        private void UpdateEvent(TypeDescription typeDescription, TypeSerialization typeSerialization, List<TypeSerializationJsonHelper> eventList)
        {
            if (!typeDescription.IsEvent || _dynamicEventHandlerDictionary.ContainsKey(typeDescription.FullName)) return;
            try
            {
                if (_filters.IsIncluded(typeDescription.FullName) ||
                typeDescription.FullName == typeof(LogFileTailEvent).FullName ||
                typeDescription.FullName == typeof(AllStatePipesProxyStatusEvent).FullName)
                {
                    var eventInstanceMgr = new TypeSerializationJsonHelper(typeSerialization, _typeSerializationConverter);
                    eventList.Add(eventInstanceMgr);
                    DynamicEventHandler dynamicEventHandler = new DynamicEventHandler(_proxy!, _eventJosonRepo, eventInstanceMgr);
                    _dynamicEventHandlerDictionary.Add(typeDescription.FullName, dynamicEventHandler);
                }
            }
            catch (Exception ex) { Log?.LogException(ex); }
        }
        private void UpdateCommand(TypeDescription typeDescription, TypeSerialization typeSerialization, List<TypeSerializationJsonHelper> cmdList)
        {
            if (!typeDescription.IsCommand || _dynamicCommandSenderDictionary.ContainsKey(typeDescription.FullName)) return;
            try
            {
                var commandInstanceMgr = new TypeSerializationJsonHelper(typeSerialization, _typeSerializationConverter);
                cmdList.Add(commandInstanceMgr);
                DynamicCommandSender dynamicCommandSender = new DynamicCommandSender(_proxy!, commandInstanceMgr);
                _dynamicCommandSenderDictionary.Add(typeDescription.FullName, dynamicCommandSender);
            }
            catch (Exception ex) { Log?.LogException(ex); }
        }
        private void UpdateServiceDescriptionEvent(SelfDescriptionEvent ev, BusConfig busConfig, bool isResponse)
        {
            _timer?.Dispose();
            _timer = null;
            _dynamicEventHandlerDictionary.Clear();
            _dynamicCommandSenderDictionary.Clear();
            List<TypeSerializationJsonHelper> cmdList = new List<TypeSerializationJsonHelper>();
            List<TypeSerializationJsonHelper> eventList = new List<TypeSerializationJsonHelper>();
            ev.TypeList.TypeSerializations.ForEach(typeSerialization =>
            {
                var typeDescription = typeSerialization.GetTopLevelDescription();
                if (typeDescription != null && typeDescription.FullName != typeof(SelfDescriptionEvent).FullName)
                {
                    UpdateEvent(typeDescription, typeSerialization, eventList);
                    UpdateCommand(typeDescription, typeSerialization, cmdList);
                }
            });
            _cmdExampleJsonRepo.SetJsonStrings(cmdList);
            _eventJosonRepo.SetEmptyJsonStrings(eventList);
            _eventJosonRepo.SetJsonString(ev);
        }
        private void Cleanup()
        {
            _timer?.Dispose();
            _timer = null;
            _proxy?.Stop();
            if (_proxy != null)
            {
                _dynamicEventHandlerDictionary.Values.ToList().ForEach(evHandler => evHandler.Dispose());
                _dynamicEventHandlerDictionary.Clear();
                _dynamicCommandSenderDictionary.Clear();
                _proxy.UnSubscribe<SelfDescriptionEvent>(UpdateServiceDescriptionEvent);
                _proxy.UnSubscribeConnectedToService(OnConnected,OnDisconnected);
            }
            _proxy?.Dispose();
            _proxy = null;
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing) Cleanup();
                disposedValue = true;
            }
        }
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
