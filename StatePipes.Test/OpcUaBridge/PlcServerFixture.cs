using Microsoft.Extensions.Logging;
using Opc.Ua;
using OpcUaServer;
using StatePipes.Comms;
using StatePipes.OpcUaBridge;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>
    /// The simulated PLC hosted in the test process on a port of its own, with nothing changing its values
    /// unless a test does. Pressure (<c>ns=?;s=pressure</c>, a plain BaseDataVariable) is the node tests monitor.
    /// </summary>
    internal sealed class PlcServerFixture : IAsyncDisposable
    {
        private readonly ITelemetryContext _telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        private readonly string _root;
        private PlcServer? _server;

        private PlcServerFixture(int port)
        {
            EndpointUrl = $"opc.tcp://localhost:{port}/PLCSimulator";
            _root = Path.Combine(Path.GetTempPath(), "PlcServerFixture", Guid.NewGuid().ToString("N"));
        }

        public string EndpointUrl { get; }
        public string PkiPath => Path.Combine(_root, "client-pki");
        public PlcNodeManager NodeManager => _server!.NodeManager!;
        public ushort NamespaceIndex => NodeManager.NamespaceIndex;
        public NodeId Pressure => new("pressure", NamespaceIndex);
        public NodeId Msg => new("msg", NamespaceIndex);
        /// <summary>A node in the Setpoints folder, which nothing but a test changes.</summary>
        public NodeId Setpoint(string name) => new($"Setpoints.{name}", NamespaceIndex);
        /// <summary>A node of the Methods object: one of its methods, objects or variables.</summary>
        public NodeId Method(string name) => new($"Methods.{name}", NamespaceIndex);
        public NodeId MethodsObject => new("Methods", NamespaceIndex);
        public NodeId Pump1 => Method("Pump1");
        public NodeId Pump2 => Method("Pump2");

        public static async Task<PlcServerFixture> StartAsync(int port)
        {
            PlcServerFixture fixture = new(port);
            await fixture.StartServerAsync();
            return fixture;
        }

        private async Task StartServerAsync() => _server = await PlcServerHost.StartAsync(EndpointUrl, Path.Combine(_root, "server"), _telemetry, CancellationToken.None);

        /// <summary>Stops the server and starts a new one on the same endpoint, as a PLC reboot would.</summary>
        public async Task RestartAsync()
        {
            await StopServerAsync();
            await StartServerAsync();
        }

        private async Task StopServerAsync()
        {
            if (_server == null) return;
            await _server.StopAsync(CancellationToken.None);
            _server.Dispose();
            _server = null;
        }

        public BridgeSettings CreateBridgeSettings(BusConfig busConfig) => new()
        {
            BusConfig = busConfig,
            OpcUaEndpoint = EndpointUrl,
            UseSecurity = false,
            AutoAcceptUntrustedCertificates = true,
            PkiPath = PkiPath,
            ApplicationName = "StatePipes.OpcUaBridge.Test",
            ReconnectDelay = TimeSpan.FromSeconds(1)
        };

        public async ValueTask DisposeAsync()
        {
            await StopServerAsync();
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
