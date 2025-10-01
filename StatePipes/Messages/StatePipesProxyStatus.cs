namespace StatePipes.Messages
{
    public class StatePipesProxyStatus(string name, bool isConnected, bool isCommunicatingWithUri)
    {
        public string Name { get; } = name;
        public bool IsConnected { get; } = isConnected;
        public bool IsCommunicatingWithUri { get; } = isCommunicatingWithUri;
    }
}
