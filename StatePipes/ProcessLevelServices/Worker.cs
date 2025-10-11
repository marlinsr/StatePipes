using Microsoft.Extensions.Hosting;
using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.ProcessLevelServices.Internal;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.ProcessLevelServices
{
    public class Worker(ServiceConfiguration defaultServiceConfiguration) : BackgroundService
    {
        private static ServiceArgs GetPostFixServiceArgs(ServiceArgs serviceArgs)
        {
            List<string> argsForPostFix = new();
            var postFix = GetValueFromEnvOrArgs(serviceArgs, "STATEPIPES_POSTFIX", ServiceArgs.PostFix);
            if (!string.IsNullOrEmpty(postFix))
            {
                argsForPostFix.Add($"{ServiceArgs.PostFix}{ServiceArgs.NameValueDelimiter}{postFix}");
                var postFixRecursiveAddToProxies = GetValueFromEnvOrArgs(serviceArgs, "STATEPIPES_POSTFIXRECURSIVEADTOPROXIES", ServiceArgs.PostFixRecursiveAddToProxies);
                if (!string.IsNullOrEmpty(postFixRecursiveAddToProxies)) 
                    argsForPostFix.Add($"{ServiceArgs.PostFixRecursiveAddToProxies}{ServiceArgs.NameValueDelimiter}{postFixRecursiveAddToProxies}");
            }
            if (argsForPostFix.Count > 0) serviceArgs = serviceArgs.Merge(new ServiceArgs(argsForPostFix));
            return serviceArgs;
        }
        private static string? GetValueFromEnvOrArgs(ServiceArgs serviceArgs, string envName, string argName) 
        {
            var argValue = serviceArgs.GetArgValue(argName);
            var envValue = Environment.GetEnvironmentVariable(envName);
            return string.IsNullOrEmpty(envValue) ? argValue : envValue;
        }
        public static void InitializeProcessLevelServices(string[] args)
        {
            var serviceArgs = new ServiceArgs(args);
            var logLevelStr = GetValueFromEnvOrArgs(serviceArgs, "STATEPIPES_LOGLEVEL", ServiceArgs.LogLevelArg);
            var companyName = GetValueFromEnvOrArgs(serviceArgs, "STATEPIPES_COMPANYNAME", ServiceArgs.CompanyName);
            serviceArgs = serviceArgs.Remove(ServiceArgs.LogLevelArg);
            serviceArgs = serviceArgs.Remove(ServiceArgs.CompanyName);
            serviceArgs = GetPostFixServiceArgs(serviceArgs);
            ArgsHolder.InitalizeArgs(serviceArgs);
            DirHelper.InitializeDirs(serviceArgs.GetArgValue(ServiceArgs.PostFix), companyName);
            InitalizeLogger();
            try
            {
                if (!string.IsNullOrEmpty(logLevelStr))
                {
                    var logLevel = Enum.Parse<ILogger.LogLevel>(logLevelStr, true);
                    Log?.SetLogLevel(logLevel);
                }
            }
            catch { Log?.LogError($"Error parsing value {logLevelStr} for {ServiceArgs.LogLevelArg}"); }
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await WorkerHelper.BackgroundServiceExecuteAsync(defaultServiceConfiguration, stoppingToken);
        }
    }
}
