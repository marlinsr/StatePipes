using StatePipes.Interfaces;

namespace StatePipes.Comms.Internal
{
    internal class StatePipesProxyFactory : IStatePipesProxyFactory, IDisposable
    {
        private readonly Dictionary<string, IStatePipesProxy> _proxyDictionary = [];
        private bool disposedValue;

        public StatePipesProxyFactory(ServiceConfiguration serviceConfiguration, IStatePipesProxyFactory? parentProxyFactory)
        {
            if (serviceConfiguration.ProxyConfigurations == null || serviceConfiguration.ProxyConfigurations.Count <= 0) return;
            //Do Substitutions and RemoteServicess
            foreach (var proxyConfiguration in serviceConfiguration.ProxyConfigurations)
            {
                if (_proxyDictionary.ContainsKey(proxyConfiguration.Name)) throw new ArgumentException($"Duplicate ProxyConfig name: {proxyConfiguration.Name}");
                if (!AddSubstitution(proxyConfiguration, serviceConfiguration.ProxySubstitutions, parentProxyFactory) && proxyConfiguration.ProxyType == ProxyConfiguration.ProxyTypeEnum.RemoteService) AddRemote(proxyConfiguration);
            }
            //Do LocalServices and LocalServiceWithRemoteAccesses without Substitutions
            foreach (var proxyConfiguration in serviceConfiguration.ProxyConfigurations)
            {
                if (!_proxyDictionary.ContainsKey(proxyConfiguration.Name) && proxyConfiguration.ServiceConfiguration.ProxySubstitutions.Count <= 0) 
                    AddService(proxyConfiguration, null);
            }
            //Do LocalServices and LocalServiceWithRemoteAccesses with Substitutions
            foreach (var proxyConfiguration in serviceConfiguration.ProxyConfigurations)
            {
                if (!_proxyDictionary.ContainsKey(proxyConfiguration.Name)) AddService(proxyConfiguration, this);
            }
        }
        private void AddService(ProxyConfiguration proxyConfiguration, IStatePipesProxyFactory? thisProxyFactory)
        {
            if (proxyConfiguration.ProxyType == ProxyConfiguration.ProxyTypeEnum.LocalServiceWithRemoteAccess)
            {
                _proxyDictionary.Add(proxyConfiguration.Name, new StatePipesService(proxyConfiguration.Name, proxyConfiguration.ServiceConfiguration, thisProxyFactory));
            }
            if (proxyConfiguration.ProxyType == ProxyConfiguration.ProxyTypeEnum.LocalService)
            {
                _proxyDictionary.Add(proxyConfiguration.Name, new StatePipesService(proxyConfiguration.Name, proxyConfiguration.ServiceConfiguration, thisProxyFactory, false));
            }
        }
        private void AddRemote(ProxyConfiguration proxyConfiguration)
        {
            if (proxyConfiguration.ProxyType == ProxyConfiguration.ProxyTypeEnum.RemoteService)
            {
                _proxyDictionary.Add(proxyConfiguration.Name, new StatePipesProxyInternal(proxyConfiguration.Name, proxyConfiguration.ServiceConfiguration.BusConfig));
            }
        }
        private bool AddSubstitution(ProxyConfiguration proxyConfiguration, IReadOnlyList<ProxySubstitution> proxySubstitutions, IStatePipesProxyFactory? parentProxyFactory)
        {
            if (parentProxyFactory == null || proxySubstitutions.Count <= 0) return false;
            var substitution = proxySubstitutions?.FirstOrDefault(s => s.ChildName == proxyConfiguration.Name);
            if (substitution == null) return false;
            var substitutionProxy = parentProxyFactory.GetStatePipesProxy(substitution.ParentName);
            if (substitutionProxy == null) return false;
            _proxyDictionary.Add(proxyConfiguration.Name, new SubstitutionProxy(proxyConfiguration.Name, (IStatePipesProxyInternal)substitutionProxy));
            return true;
        }
        public List<IStatePipesProxy> GetAllClientProxies() => [.. _proxyDictionary.Values];
        public IStatePipesProxy? GetStatePipesProxy(string name, bool connect = true) => _proxyDictionary.TryGetValue(name, out IStatePipesProxy? value) ? value : null;
        public List<string> GetProxyConfigNames() => [.. _proxyDictionary.Keys];

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    _proxyDictionary.Values.ToList().ForEach(p => { try { p.Dispose(); } catch { } });
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
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
