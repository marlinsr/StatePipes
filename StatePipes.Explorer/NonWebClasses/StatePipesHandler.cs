namespace StatePipes.Explorer.NonWebClasses
{
    public class StatePipesHandler : IStatePipesHandler
    {
        private readonly Dictionary<Guid, StatePipesHandlerHelper> _helperDictionary = [];
        private StatePipesHandlerHelper GetHelper(Guid instanceGuid)
        {
            lock (_helperDictionary)
            {
                if (_helperDictionary.ContainsKey(instanceGuid))
                {
                    return _helperDictionary[instanceGuid];
                }
                else
                {
                    var helper = new StatePipesHandlerHelper();
                    _helperDictionary.Add(instanceGuid, helper);
                    return helper;
                }
            }
        }
        public void Initialize(Guid instanceGuid, string brokerUri, string exchangeName, ExcludeAndIncludeLists filters, string? clientCertFileName, string? clientCertPasswordFileName, string? hashedPassword)
        {
            GetHelper(instanceGuid).Initialize(brokerUri, exchangeName, filters, clientCertFileName, clientCertPasswordFileName, hashedPassword);
        }
        public List<EventEntry> GetEventJsons(Guid instanceGuid) => GetHelper(instanceGuid).EventJsons;
        public List<CommandEntry> GetCommandList(Guid instanceGuid) => GetHelper(instanceGuid).CommandList;
        public bool GetIsConnectedToBroker(Guid instanceGuid) => GetHelper(instanceGuid).IsConnectedToBroker;
        public void SendCommand(Guid instanceGuid, string commandTypeFullName, string commandJson)
        {
            GetHelper(instanceGuid).SendCommand(commandTypeFullName, commandJson);
        }
        public dynamic? TypeDefault(Guid instanceGuid, string commandTypeFullName, Type t)
        {
            return GetHelper(instanceGuid).TypeDefault(commandTypeFullName, t);
        }
        public object? GetCommandObject(Guid instanceGuid, string commandTypeFullName, string commandJson)
        {
            return GetHelper(instanceGuid).GetCommandObject(commandTypeFullName, commandJson);
        }
        public void ResetJson(Guid instanceGuid, string commandTypeFullName) => GetHelper(instanceGuid).ResetJson(commandTypeFullName);
        public void Close(Guid instanceGuid)
        {
            lock (_helperDictionary)
            {
                var helper = GetHelper(instanceGuid);
                _helperDictionary.Remove(instanceGuid);
                helper.Dispose();
            }
        }
    }
}
