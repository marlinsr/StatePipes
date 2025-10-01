using Newtonsoft.Json;

namespace StatePipes.Comms
{
    public class BusConfig : IEquatable<BusConfig>
    {
        public string BrokerUri { get; }
        public string ExchangeNamePrefix { get; }
        public string ClientCertPath { get; }
        public string ClientCertPasswordPath { get; }
        [JsonIgnore]
        public string CommandExchangeName => $"{ExchangeNamePrefix}{ExchangeNamePostfix}.commands";
        [JsonIgnore]
        public string EventExchangeName => $"{ExchangeNamePrefix}{ExchangeNamePostfix}.events";
        [JsonIgnore]
        public string ResponseExchangeName => $"{ExchangeNamePrefix}{ExchangeNamePostfix}.{ResponseExchangeGuid}.responses";
        public string ResponseExchangeGuid { get; }
        public string ExchangeNamePostfix { get; private set; }
        public BusConfig? PreviousHop { get; }
        [JsonConstructor]
        public BusConfig(string brokerUri, string exchangeNamePrefix, string clientCertPath, string clientCertPasswordPath, string responseExchangeGuid, string exchangeNamePostfix, BusConfig? previousHop)
        {
            BrokerUri = brokerUri;
            ExchangeNamePrefix = exchangeNamePrefix;
            ClientCertPath = clientCertPath;
            ClientCertPasswordPath = clientCertPasswordPath;
            ResponseExchangeGuid = string.IsNullOrEmpty(responseExchangeGuid) ? Guid.NewGuid().ToString("N") : responseExchangeGuid;
            ExchangeNamePostfix = exchangeNamePostfix;
            PreviousHop = previousHop;
        }
        public BusConfig(BusConfig config, BusConfig previousHop) : this(config.BrokerUri, config.ExchangeNamePrefix, config.ClientCertPath, config.ClientCertPasswordPath, config.ResponseExchangeGuid, config.ExchangeNamePostfix, previousHop){}
        public BusConfig(string brokerUri, string exchangeNamePrefix, string clientCertPath, string clientCertPasswordPath, string exchangeNamePostfix) : this(brokerUri, exchangeNamePrefix, clientCertPath, clientCertPasswordPath, string.Empty, exchangeNamePostfix, null) { }
        public BusConfig(string brokerUri, string exchangeNamePrefix, string clientCertPath, string clientCertPasswordPath) : this(brokerUri, exchangeNamePrefix, clientCertPath, clientCertPasswordPath, string.Empty, string.Empty, null) { }

        public void SetExchangeNamePostfix(string exchangeNamePostfix) => ExchangeNamePostfix = exchangeNamePostfix;
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
