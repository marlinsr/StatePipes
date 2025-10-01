using Newtonsoft.Json;
using StatePipes.ProcessLevelServices;
namespace StatePipes.Comms
{
    public class ServiceConfiguration
    {
        [JsonConstructor]
        public ServiceConfiguration(BusConfig busConfig, string assemblyName, string containerSetupClassLibraryTypeFullName, IReadOnlyList<ProxyConfiguration> proxyConfigurations, IReadOnlyList<ProxySubstitution> proxySubstitutions, ServiceArgs args)
        {
            BusConfig = busConfig;
            AssemblyName = assemblyName;
            ProxyConfigurations = proxyConfigurations;
            Args = args;
            ContainerSetupClassLibraryTypeFullName = containerSetupClassLibraryTypeFullName;
            ProxySubstitutions = proxySubstitutions;
        }
        public void MergeCommandLineArgs(ServiceArgs args) => Args.Merge(args);
        public BusConfig BusConfig { get; }
        public string AssemblyName { get; }
        public string ContainerSetupClassLibraryTypeFullName { get; }
        public IReadOnlyList<ProxyConfiguration> ProxyConfigurations { get; }
        public IReadOnlyList<ProxySubstitution> ProxySubstitutions { get; }
        public ServiceArgs Args { get; private set; }
    }
}
