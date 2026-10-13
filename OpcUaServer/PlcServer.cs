using Opc.Ua;
using Opc.Ua.Server;

namespace OpcUaServer;

public class PlcServer : StandardServer
{
    public PlcNodeManager? NodeManager { get; private set; }

    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration)
    {
        NodeManager = new PlcNodeManager(server, configuration);
        return new MasterNodeManager(server, configuration, null, NodeManager);
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
