using StatePipes.Comms;
using StatePipes.Comms.Internal;
using System.Diagnostics;
using System.Reflection;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.ProcessLevelServices.Internal
{
    internal class WorkerHelper
    {
        public static Assembly GetTopLevelAssembly()
        {
            var assembly = Assembly.GetEntryAssembly();
            if (assembly == null) assembly = Assembly.GetCallingAssembly();
            if (assembly == null) throw new Exception("Top Level Assembly Not Found");
            return assembly;
        }
        public static string FileName()
        {
            var _objParentAssembly = GetTopLevelAssembly();
            if (File.Exists(_objParentAssembly.Location)) return _objParentAssembly.Location;
            if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + AppDomain.CurrentDomain.FriendlyName))
                return AppDomain.CurrentDomain.BaseDirectory + AppDomain.CurrentDomain.FriendlyName;
            if (File.Exists(Assembly.GetExecutingAssembly().Location)) return Assembly.GetExecutingAssembly().Location;
            throw new IOException("Assembly not found");
        }
        public static string ExeName() => Path.GetFileName(FileName());
        private static ServiceConfiguration GetServiceConfigurationFromFile(ServiceConfiguration defaultServiceConfiguration)
        {
            JsonFileHelperUtility.CreateConfigDirectory();
            var serviceConfiguration = JsonFileHelperUtility.ReadFile<ServiceConfiguration>();
            if (serviceConfiguration != null) return serviceConfiguration;
            return defaultServiceConfiguration;
        }
        private static void MergeArgs(ServiceArgs args, ServiceConfiguration serviceConfiguration, string? exchangePostFix)
        {
            serviceConfiguration.BusConfig.SetExchangeNamePostfix(serviceConfiguration.BusConfig.ExchangeNamePostfix + exchangePostFix);
            serviceConfiguration.MergeCommandLineArgs(args);
            foreach(var proxyConfig in serviceConfiguration.ProxyConfigurations)
            {
                MergeArgs(args, proxyConfig.ServiceConfiguration, exchangePostFix);
            }
        }
        public static async Task WaitForCancellation(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    Log?.LogVerbose($"Worker running at: {DateTimeOffset.Now}");
                    await Task.Delay(int.MaxValue, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        public static async Task BackgroundServiceExecuteAsync(ServiceConfiguration defaultServiceConfiguration, CancellationToken stoppingToken)
        {
            var serviceConfiguration = defaultServiceConfiguration;
            var usedefaultserviceconfig = !string.IsNullOrEmpty(ArgsHolder.Args?.GetArgValue(ServiceArgs.UseDefaultConfig));
            if (!usedefaultserviceconfig)
            {
                serviceConfiguration = GetServiceConfigurationFromFile(defaultServiceConfiguration);
                JsonFileHelperUtility.SaveFile(serviceConfiguration);
            }
            var postFix = ArgsHolder.Args?.GetArgValue(ServiceArgs.PostFix);
            var args = ArgsHolder.Args?.Remove(ServiceArgs.PostFix) ?? new ServiceArgs(null);
            MergeArgs(args, serviceConfiguration, postFix);
            var exeName = ExeName();
            var version = FileVersionInfo.GetVersionInfo(FileName()).ProductVersion;
            Log?.LogInfo($"Starting as service {exeName} [version {version}]...");
            StatePipesService topLevelService = new(serviceConfiguration);
            topLevelService.Start();
            await WaitForCancellation(stoppingToken);
            topLevelService.Dispose();
        }
    }
}
