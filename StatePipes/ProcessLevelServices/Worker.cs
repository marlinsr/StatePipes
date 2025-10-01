using Microsoft.Extensions.Hosting;
using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.ProcessLevelServices.Internal;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.ProcessLevelServices
{
    public class Worker(ServiceConfiguration defaultServiceConfiguration) : BackgroundService
    {
        public static void InitializeProcessLevelServices(string[] args)
        {
            var serviceArgs = new ServiceArgs(args);
            var logLevelStr = serviceArgs.GetArgValue(ServiceArgs.LogLevelArg);
            var logLevelEnv = Environment.GetEnvironmentVariable("STATEPIPES_COMPANYNAME");
            logLevelStr = string.IsNullOrEmpty(logLevelEnv) ? logLevelStr : logLevelEnv;
            serviceArgs = serviceArgs.Remove(ServiceArgs.LogLevelArg);
            var postFix = serviceArgs.GetArgValue(ServiceArgs.PostFix);
            var postFixEnv = Environment.GetEnvironmentVariable("STATEPIPES_POSTFIX");
            postFix = string.IsNullOrEmpty(postFixEnv) ? postFix : postFixEnv;
            var companyName = serviceArgs.GetArgValue(ServiceArgs.CompanyName);
            var companyNameEnv = Environment.GetEnvironmentVariable("STATEPIPES_COMPANYNAME");
            companyName = string.IsNullOrEmpty(companyNameEnv) ? companyName : companyNameEnv;
            serviceArgs = serviceArgs.Remove(ServiceArgs.CompanyName);
            ArgsHolder.InitalizeArgs(serviceArgs);
            DirHelper.InitializeDirs(postFix, companyName);
            InitalizeLogger();
            try
            {
                if (!string.IsNullOrEmpty(logLevelStr))
                {
                    var logLevel = Enum.Parse<ILogger.LogLevel>(logLevelStr, true);
                    Log?.SetLogLevel(logLevel);
                }
            }
            catch
            {
                Log?.LogError($"Error parsing value {logLevelStr} for {ServiceArgs.LogLevelArg}");
            }
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await WorkerHelper.BackgroundServiceExecuteAsync(defaultServiceConfiguration, stoppingToken);
        }
    }
}
