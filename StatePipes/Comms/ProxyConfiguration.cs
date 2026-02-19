namespace StatePipes.Comms
{
    public class ProxyConfiguration(string name, ProxyConfiguration.ProxyTypeEnum proxyType, ServiceConfiguration serviceConfiguration)
    {
        public enum ProxyTypeEnum { RemoteService, LocalService, LocalServiceWithRemoteAccess };
        public string Name { get; } = name;
        public ProxyTypeEnum ProxyType { get; } = proxyType;
        public ServiceConfiguration ServiceConfiguration { get; } = serviceConfiguration;

    }
}
