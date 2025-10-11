using Newtonsoft.Json;
using StatePipes.Common;
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
        internal void MergeCommandLineArgs(ServiceArgs args)
        {
            Args.Merge(args);
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
        public BusConfig BusConfig { get; }
        public string AssemblyName { get; }
        public string ContainerSetupClassLibraryTypeFullName { get; }
        public IReadOnlyList<ProxyConfiguration> ProxyConfigurations { get; }
        public IReadOnlyList<ProxySubstitution> ProxySubstitutions { get; }
        public ServiceArgs Args { get; private set; }
    }
}
