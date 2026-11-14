using Opc.Ua;
using Opc.Ua.Server;

namespace OpcUaServer;

public class PlcServer : StandardServer
{
    public PlcServer(ITelemetryContext telemetry) : base(telemetry) => AddNodeManager(new PlcNodeManagerFactory(this));

    /// <summary>Set when the server starts and its node manager factory runs.</summary>
    public PlcNodeManager? NodeManager { get; private set; }

    /// <summary>Hands PlcNodeManager to the server's startup pipeline and keeps a reference to the instance it creates.</summary>
    private sealed class PlcNodeManagerFactory(PlcServer owner) : INodeManagerFactory
    {
        public ArrayOf<string> NamespacesUris => [PlcNodeManager.NamespaceUri];

        public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration) =>
            owner.NodeManager = new PlcNodeManager(server, configuration);
    }

    /// <summary>Closes every session whose name <paramref name="matches"/>, as an administrator or a server restart might.</summary>
    public async Task CloseSessionsAsync(Func<string, bool> matches, CancellationToken ct)
    {
        foreach (var session in CurrentInstance.SessionManager.GetSessions().Where(s => matches(s.SessionName ?? string.Empty)).ToList())
            await CurrentInstance.SessionManager.CloseSessionAsync(session.Id, ct);
    }

    protected override ServerProperties LoadServerProperties()
    {
        return new ServerProperties
        {
            ManufacturerName = "PLCSimulator",
            ProductName = "PLCSimulator OPC UA Server",
            ProductUri = "urn:PLCSimulator:OpcUaServer",
            SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
            BuildNumber = Utils.GetAssemblyBuildNumber(),
            BuildDate = Utils.GetAssemblyTimestamp()
        };
    }
}
