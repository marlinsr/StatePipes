using System.Reflection;

namespace StatePipes.ServiceCreatorTool
{
    internal class BaseToolGenerator(string solutionDir, string solutionFileName)
    {
        protected readonly PathHelper _pathProvider = new(solutionDir);
        protected const string solutionNameMoniker = "@#$SolutionName@#$";
        protected const string solutionInjection1Moniker = "# Injection Point 1, Do Not Delete or Modify";
        protected const string solutionInjection2Moniker = "# Injection Point 2, Do Not Delete or Modify";
        protected const string proxyInjection1Moniker = "// Injection Point 1, Do Not Delete or Modify";
        protected const string proxyInjection2Moniker = "// Injection Point 2, Do Not Delete or Modify";
        protected const string proxyInjection3Moniker = "// Injection Point 3, Do Not Delete or Modify";
        protected const string proxyInjection4Moniker = "// Injection Point 4, Do Not Delete or Modify";
        protected const string containerInjection1Moniker = "# Injection Point 1, Do Not Delete or Modify";
        protected const string guardInjection1Moniker = "// Injection Point 5, Do Not Delete or Modify";
        protected const string publicTriggerUsingCommentMoniker = "//@#$PublicTriggers@#$";
        protected const string internalTriggerUsingCommentMoniker = "//@#$InternalTriggers@#$";
        public const string packagePrefix = "Packages.";
        public const string solutionExtension = ".sln";
        public const string projectExtension = ".csproj";
        public string SolutionFileName { get; private set; } = solutionFileName;
        public string SolutionFullPath => Path.Combine(_pathProvider.GetPath(PathName.Solution), SolutionFileName);
        public string SolutionDir => _pathProvider.GetPath(PathName.Solution);
        public string SolutionNameNoExtension => GetSolutionNameNoExtension(SolutionFileName);
        protected static string GetProductName(string solutionName)
        {
            solutionName = GetSolutionNameNoPackages(GetSolutionNameNoExtension(solutionName));
            if (solutionName.Contains('.')) solutionName = solutionName[(solutionName.IndexOf('.') + 1)..];
            return solutionName;
        }
        protected static string GetCompanyName(string solutionName)
        {
            solutionName = GetSolutionNameNoPackages(GetSolutionNameNoExtension(solutionName));
            if (solutionName.Contains('.')) solutionName = solutionName[..solutionName.IndexOf('.')];
            return solutionName;
        }
        public static string GetProjectNameNoExtension(string projectName) =>
            projectName.EndsWith(projectExtension, StringComparison.InvariantCultureIgnoreCase) ? projectName[..projectName.LastIndexOf(projectExtension, StringComparison.InvariantCultureIgnoreCase)] : projectName;
        public static string GetSolutionNameNoExtension(string solutionName) =>
            solutionName.EndsWith(solutionExtension, StringComparison.InvariantCultureIgnoreCase) ? solutionName[..solutionName.LastIndexOf(solutionExtension, StringComparison.InvariantCultureIgnoreCase)] : solutionName;
        public static string GetSolutionNameNoPackages(string solutionName) =>
            solutionName.StartsWith(packagePrefix, StringComparison.InvariantCultureIgnoreCase) ? solutionName[(solutionName.IndexOf(packagePrefix, StringComparison.InvariantCultureIgnoreCase) + packagePrefix.Length)..] : solutionName;
        protected static string GetComponentName(string classLibraryName) => classLibraryName.Contains('.') ? classLibraryName[(classLibraryName.LastIndexOf('.') + 1)..] : classLibraryName;
        protected static string GetDefaultStateMachineName(string classLibraryName) => $"{GetComponentName(classLibraryName)}StateMachine";
        protected static MonikerSubstitution CreateMonikers(string solutionName, string projectName)
        {
            //Define Monikers
            var monikers = new MonikerSubstitution();
            monikers.AddMoniker("@#$Year@#$", DateTime.Now.Year.ToString());
            monikers.AddMoniker(solutionNameMoniker, solutionName);
            monikers.AddMoniker("@#$ClassLibraryName@#$", projectName);
            monikers.AddMoniker("@#$ServiceProjectGuid@#$", Guid.NewGuid().ToString("B"));
            monikers.AddMoniker("@#$ClassLibraryProjectGuid@#$", Guid.NewGuid().ToString("B"));
            monikers.AddMoniker("@#$TestProjectGuid@#$", Guid.NewGuid().ToString("B"));
            monikers.AddMoniker("@#$SolutionItemsGuid@#$", Guid.NewGuid().ToString("B"));
            monikers.AddMoniker("@#$SolutionGuid@#$", Guid.NewGuid().ToString("B"));
            monikers.AddMoniker("@#$SecretsGuid@#$", Guid.NewGuid().ToString("D"));
            monikers.AddMoniker("@#$ContainerImageName@#$", projectName.Replace(".", "-").ToLower() + "-service");
            monikers.AddMoniker("@#$BuildFunctionName@#$", projectName.Replace(".", "_").ToLower() + "_Service_LocalImageBuild");
            monikers.AddMoniker("@#$StateMachineName@#$", GetDefaultStateMachineName(projectName));
            monikers.AddMoniker("@#$NetworkName@#$", $"{GetCompanyName(solutionName)}-net");
            monikers.AddMoniker("@#$SolutionInjection1@#", solutionInjection1Moniker);
            monikers.AddMoniker("@#$SolutionInjection2@#", solutionInjection2Moniker);
            monikers.AddMoniker("@#$ProxyInjection1@#$", proxyInjection1Moniker);
            monikers.AddMoniker("@#$ProxyInjection2@#$", proxyInjection2Moniker);
            monikers.AddMoniker("@#$ProxyInjection3@#$", proxyInjection3Moniker);
            monikers.AddMoniker("@#$ProxyInjection4@#$", proxyInjection4Moniker);
            monikers.AddMoniker("@#$ContainerInjection1@#$", containerInjection1Moniker);
            monikers.AddMoniker("@#$CompanyName@#$", GetCompanyName(solutionName));
            monikers.AddMoniker("@#$ProductName@#$", GetProductName(solutionName));
            monikers.AddMoniker("@#$EventExchange@#$", $"{projectName}.Service.V1.Event");
            monikers.AddMoniker("@#$InboundExchange@#$", $"{projectName}.Service.V1.Inbound");
            monikers.AddMoniker("@#$BrokerUriForStartScript@#$", "amqps:%2F%2Famqp09-broker%2FProduction");
            monikers.AddMoniker("@#$BrokerUri@#$", "amqps://amqp09-broker/Production");
            monikers.AddMoniker("@#$CertFileName@#$", "amqpuser.amqp09-broker.client.p12");
            monikers.AddMoniker("@#$StatePipesVersion@#$", Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+').First() ?? string.Empty);
            return monikers;
        }
        protected static bool IsServiceProject(string projectFileName)
        {
            if (projectFileName.EndsWith(".Service.csproj"))
            {
                Console.WriteLine("Service project highlighted, need to be in the class library!");
                return false;
            }
            return true;
        }
}
}
