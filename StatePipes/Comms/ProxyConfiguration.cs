namespace StatePipes.Comms
{
    public class ProxyConfiguration
    {
        public ProxyConfiguration(string name, ProxyTypeEnum proxyType, ServiceConfiguration serviceConfiguration)
        {
            Name = name;
            ProxyType = proxyType;
            ServiceConfiguration = serviceConfiguration;
        }
        public enum ProxyTypeEnum { RemoteService, LocalService, LocalServiceWithRemoteAccess };
        public string Name { get; }
        public ProxyTypeEnum ProxyType { get; }
        public ServiceConfiguration ServiceConfiguration { get; }

    }
}
