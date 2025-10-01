namespace StatePipes.Explorer.NonWebClasses
{
    public interface IStatePipesHandler
    {
        List<EventEntry> GetEventJsons(Guid instanceGuid);
        List<CommandEntry> GetCommandList(Guid instanceGuid);
        bool GetIsConnectedToBroker(Guid instanceGuid);
        void SendCommand(Guid instanceGuid, string commandTypeFullName, string commandJson);
        void ResetJson(Guid instanceGuid, string commandTypeFullName);
        void Initialize(Guid instanceGuid, string brokerUri, string exchangeName, ExcludeAndIncludeLists filters, string? clientCertFileName, string? clientCertPasswordFileName, string? hashedPassword);
        void Close(Guid instanceGuid);
        object? GetCommandObject(Guid instanceGuid, string commandTypeFullName, string commandJson);
        dynamic? TypeDefault(Guid instanceGuid, string commandTypeFullName, Type t);
    }
}
