using StatePipes.BrokerProxy;
using StatePipes.ProcessLevelServices;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;

Worker.InitializeProcessLevelServices(args);
var builder = Host.CreateApplicationBuilder(args);
if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    builder.Services.AddWindowsService();
}
builder.Services.AddHostedService<ServiceWorker>();

var host = builder.Build();
host.Run();

