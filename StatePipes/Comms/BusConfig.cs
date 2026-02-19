using Newtonsoft.Json;
using StatePipes.Common;

namespace StatePipes.Comms
{
    [method: JsonConstructor]
    public class BusConfig(string brokerUri, string exchangeNamePrefix, string clientCertPath, string clientCertPasswordPath, string responseExchangeGuid, string exchangeNamePostfix, BusConfig? previousHop) : IEquatable<BusConfig>
    {
        public string BrokerUri { get; } = brokerUri;
        public string ExchangeNamePrefix { get; } = exchangeNamePrefix;
        public string ClientCertPath { get; } = clientCertPath;
        public string ClientCertPasswordPath { get; } = clientCertPasswordPath;
        [JsonIgnore]
        public string CommandExchangeName => $"{ExchangeNamePrefix}{ExchangeNamePostfix}.commands";
        [JsonIgnore]
        public string EventExchangeName => $"{ExchangeNamePrefix}{ExchangeNamePostfix}.events";
        [JsonIgnore]
        public string ResponseExchangeName => $"{ExchangeNamePrefix}{ExchangeNamePostfix}.{ResponseExchangeGuid}.responses";
        public string ResponseExchangeGuid { get; } = string.IsNullOrEmpty(responseExchangeGuid) ? Guid.NewGuid().ToString("N") : responseExchangeGuid;
        public string ExchangeNamePostfix { get; private set; } = exchangeNamePostfix;
        public BusConfig? PreviousHop { get; } = previousHop;

        public BusConfig(BusConfig config, BusConfig previousHop) : this(config.BrokerUri, config.ExchangeNamePrefix, config.ClientCertPath, config.ClientCertPasswordPath, config.ResponseExchangeGuid, config.ExchangeNamePostfix, previousHop){}
        public BusConfig(string brokerUri, string exchangeNamePrefix, string clientCertPath, string clientCertPasswordPath, string exchangeNamePostfix) : this(brokerUri, exchangeNamePrefix, clientCertPath, clientCertPasswordPath, string.Empty, exchangeNamePostfix, null) { }
        public BusConfig(string brokerUri, string exchangeNamePrefix, string clientCertPath, string clientCertPasswordPath) : this(brokerUri, exchangeNamePrefix, clientCertPath, clientCertPasswordPath, string.Empty, string.Empty, null) { }

        internal void SetExchangeNamePostfix(string exchangeNamePostfix) => ExchangeNamePostfix = exchangeNamePostfix;
        public BusConfig AddPostfix(string postfix)
        {
            BusConfig clonedBusConfig = JsonUtility.Clone(this)!;
            clonedBusConfig.SetExchangeNamePostfix(postfix);
            return clonedBusConfig;
        }
        public bool Equals(BusConfig? other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return BrokerUri == other.BrokerUri && ExchangeNamePrefix == other.ExchangeNamePrefix && ClientCertPath == other.ClientCertPath && ClientCertPasswordPath == other.ClientCertPasswordPath && ResponseExchangeGuid == other.ResponseExchangeGuid && ExchangeNamePostfix == other.ExchangeNamePostfix;
        }
        public override bool Equals(object? obj)
        {
            if (obj == null) return false;
            if (obj is BusConfig busConfig) return Equals(busConfig);
            return false;
        }
        public override int GetHashCode() => HashCode.Combine(BrokerUri, ExchangeNamePrefix, ClientCertPath, ClientCertPasswordPath, ResponseExchangeGuid, ExchangeNamePostfix);
    }
}
