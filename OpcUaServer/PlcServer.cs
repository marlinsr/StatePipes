using Opc.Ua;
using Opc.Ua.Server;

namespace OpcUaServer;

public class PlcServer(ITelemetryContext telemetry) : StandardServer(telemetry)
{
    public PlcNodeManager? NodeManager { get; private set; }

    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration)
    {
        NodeManager = new PlcNodeManager(server, configuration);
        return new MasterNodeManager(server, configuration, null, NodeManager);
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
