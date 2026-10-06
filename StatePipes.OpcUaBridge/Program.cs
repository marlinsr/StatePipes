using StatePipes.OpcUaBridge;
using StatePipes.ProcessLevelServices;
using System.Runtime.InteropServices;

Worker.InitializeProcessLevelServices(args);
var builder = Host.CreateApplicationBuilder(args);
if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    builder.Services.AddWindowsService();
}
builder.Services.AddHostedService<ServiceWorker>();

var host = builder.Build();
host.Run();
