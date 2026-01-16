using StatePipes.Comms;
using StatePipes.ProcessLevelServices;
using StatePipes.ProcessLevelServices.Internal;
using System.Diagnostics;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.BrokerProxy
{
    internal class ServiceWorker : BackgroundService
    {
        private static string GetValueFromEnvOrArgs(string argName)
        {
            string argValue = ArgsHolder.Args?.GetArgValue(argPrefix + argName) ?? string.Empty;
            var envValue = Environment.GetEnvironmentVariable(argName);
            return string.IsNullOrEmpty(envValue) ? argValue : envValue;
        }
        private const string argPrefix = "--";

        private static (BusConfig ProxyBusConfig, BusConfig ServiceBusConfig) ConfigureBusConfigs()
        {
            BusConfig proxyBusConfig = new(GetValueFromEnvOrArgs("SOURCEBROKER"), GetValueFromEnvOrArgs("SOURCEEXCHANGE"),
                GetValueFromEnvOrArgs("SOURCECERTPATH"), GetValueFromEnvOrArgs("SOURCEPWPATH"));
            BusConfig serviceBusConfig = new(GetValueFromEnvOrArgs("PROXYBROKER"), GetValueFromEnvOrArgs("PROXYEXCHANGE"),
                GetValueFromEnvOrArgs("PROXYCERTPATH"), GetValueFromEnvOrArgs("PROXYPWPATH"));
            return (proxyBusConfig, serviceBusConfig);
        }
        public static async Task BackgroundServiceExecuteAsync(CancellationToken stoppingToken)
        {
            var exeName = WorkerHelper.ExeName();
            var version = FileVersionInfo.GetVersionInfo(WorkerHelper.FileName()).ProductVersion;
            Log?.LogInfo($"Starting as service {exeName} [version {version}]...");
            var busConfigs = ConfigureBusConfigs();
            SimpleStatePipesProxy proxy = new("BrokerProxyServiceProxy", busConfigs.ProxyBusConfig);
            SimpleStatePipesService topLevelService = new(busConfigs.ServiceBusConfig, proxy);
            topLevelService.StartLongRunning();
            proxy.Start();
            await WorkerHelper.WaitForCancellation(stoppingToken);
            topLevelService.Dispose();
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await BackgroundServiceExecuteAsync(stoppingToken);
        }
    }
}
