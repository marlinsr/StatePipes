using Newtonsoft.Json;
using StatePipes.Common;
using StatePipes.ProcessLevelServices;
namespace StatePipes.Comms
{
    [method: JsonConstructor]
    public class ServiceConfiguration(BusConfig busConfig, string assemblyName, string containerSetupClassLibraryTypeFullName, IReadOnlyList<ProxyConfiguration> proxyConfigurations, IReadOnlyList<ProxySubstitution> proxySubstitutions, ServiceArgs args)
    {
        internal void MergeCommandLineArgs(ServiceArgs args)
        {
            Args = Args.Merge(args);
            foreach (var proxyConfig in this.ProxyConfigurations)
            {
                proxyConfig.ServiceConfiguration.MergeCommandLineArgs(args);
            }
        }
        internal void AddPostfixWorker(string? postfix, bool recursiveAddToProxies)
        {
            if (string.IsNullOrEmpty(postfix)) return;
            this.BusConfig.SetExchangeNamePostfix(BusConfig.ExchangeNamePostfix + postfix);
            if (!recursiveAddToProxies) return;
            foreach (var proxyConfig in this.ProxyConfigurations)
            {
                proxyConfig.ServiceConfiguration.AddPostfixWorker(postfix, recursiveAddToProxies);
            }
        }
        public ServiceConfiguration AddPostfix(string postfix, bool recursiveAddToProxies = false)
        {
            ServiceConfiguration clonedServiceConfiguration = JsonUtility.Clone(this)!;
            clonedServiceConfiguration.AddPostfixWorker(postfix, recursiveAddToProxies);
            return clonedServiceConfiguration;
        }
        public BusConfig BusConfig { get; } = busConfig;
        public string AssemblyName { get; } = assemblyName;
        public string ContainerSetupClassLibraryTypeFullName { get; } = containerSetupClassLibraryTypeFullName;
        public IReadOnlyList<ProxyConfiguration> ProxyConfigurations { get; } = proxyConfigurations;
        public IReadOnlyList<ProxySubstitution> ProxySubstitutions { get; } = proxySubstitutions;
        public ServiceArgs Args { get; private set; } = args;
    }
}
