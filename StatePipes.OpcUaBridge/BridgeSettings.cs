using StatePipes.Comms;
using StatePipes.ProcessLevelServices;
using System.Text.RegularExpressions;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Everything the bridge needs to know, read once at startup. Each value comes from an environment variable
    /// (the docker way) and falls back to a <c>--NAME=value</c> command line argument, matching BrokerProxy.
    ///
    /// <para>The StatePipes side reuses BrokerProxy's names (<c>BROKER</c>, <c>EXCHANGE</c>, <c>CERTPATH</c>,
    /// <c>PWPATH</c>) so a deployment can describe either container the same way. The broker kind follows from
    /// the BROKER URI exactly as it does everywhere else: <c>ssl://</c> is Kafka, anything else RabbitMQ.</para>
    /// </summary>
    internal sealed class BridgeSettings
    {
        private const string ArgPrefix = "--";

        public required BusConfig BusConfig { get; init; }
        public required string OpcUaEndpoint { get; init; }
        public bool UseSecurity { get; init; } = true;
        public string? UserName { get; init; }
        public string? Password { get; init; }
        public bool AutoAcceptUntrustedCertificates { get; init; }
        public required string PkiPath { get; init; }
        public required string ApplicationName { get; init; }
        public bool IncludeNamespaceZero { get; init; }
        /// <summary>When set, only nodes whose NodeId text (e.g. <c>ns=2;s=Line1.Temperature</c>) matches are exposed.</summary>
        public Regex? NodeFilter { get; init; }
        public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

        public static BridgeSettings Load()
        {
            var endpoint = Get("OPCUA_ENDPOINT");
            if (string.IsNullOrEmpty(endpoint)) throw new InvalidOperationException("OPCUA_ENDPOINT must be set, e.g. opc.tcp://plc:4840");
            var broker = Get("BROKER");
            var exchange = Get("EXCHANGE");
            if (string.IsNullOrEmpty(broker) || string.IsNullOrEmpty(exchange)) throw new InvalidOperationException("BROKER and EXCHANGE must be set");
            return new BridgeSettings
            {
                BusConfig = new BusConfig(broker, exchange, Get("CERTPATH"), Get("PWPATH")),
                OpcUaEndpoint = endpoint,
                UseSecurity = GetBool("OPCUA_USE_SECURITY", true),
                UserName = NullIfEmpty(Get("OPCUA_USERNAME")),
                Password = NullIfEmpty(Get("OPCUA_PASSWORD")) ?? ReadSecretFile(Get("OPCUA_PASSWORD_PATH")),
                AutoAcceptUntrustedCertificates = GetBool("OPCUA_AUTO_ACCEPT_UNTRUSTED", false),
                PkiPath = NullIfEmpty(Get("OPCUA_PKI_PATH")) ?? Path.Combine(AppContext.BaseDirectory, "pki"),
                ApplicationName = NullIfEmpty(Get("OPCUA_APPLICATION_NAME")) ?? "StatePipes.OpcUaBridge",
                IncludeNamespaceZero = GetBool("OPCUA_INCLUDE_NS0", false),
                NodeFilter = string.IsNullOrEmpty(Get("OPCUA_NODE_FILTER")) ? null : new Regex(Get("OPCUA_NODE_FILTER"), RegexOptions.Compiled | RegexOptions.CultureInvariant),
                ReconnectDelay = TimeSpan.FromSeconds(GetInt("OPCUA_RECONNECT_SECONDS", 5)),
            };
        }

        private static string Get(string name)
        {
            var envValue = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(envValue)) return envValue;
            return ArgsHolder.Args?.GetArgValue(ArgPrefix + name) ?? string.Empty;
        }
        private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
        private static bool GetBool(string name, bool defaultValue) => bool.TryParse(Get(name), out var value) ? value : defaultValue;
        private static int GetInt(string name, int defaultValue) => int.TryParse(Get(name), out var value) && value > 0 ? value : defaultValue;
        private static string? ReadSecretFile(string path) => string.IsNullOrEmpty(path) ? null : File.ReadAllText(path).Trim();
    }
}
