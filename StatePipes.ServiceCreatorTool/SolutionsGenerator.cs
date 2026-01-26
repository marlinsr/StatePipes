using System.Reflection;

namespace StatePipes.ServiceCreatorTool
{
    internal class SolutionsGenerator
    {
        private readonly GeneratorHelper _helper;
        private readonly PathHelper _pathProvider;
        private const string solutionNameMoniker = "@#$SolutionName@#$";
        private const string solutionInjection1Moniker = "# Injection Point 1, Do Not Delete or Modify";
        private const string solutionInjection2Moniker = "# Injection Point 2, Do Not Delete or Modify";
        private const string proxyInjection1Moniker = "// Injection Point 1, Do Not Delete or Modify";
        private const string proxyInjection2Moniker = "// Injection Point 2, Do Not Delete or Modify";
        private const string proxyInjection3Moniker = "// Injection Point 3, Do Not Delete or Modify";
        private const string proxyInjection4Moniker = "// Injection Point 4, Do Not Delete or Modify";
        private const string containerInjection1Moniker = "# Injection Point 1, Do Not Delete or Modify";
        public const string packagePrefix = "Packages.";
        public const string solutionExtension = ".sln";
        public const string projectExtension = ".csproj";
        public string SolutionFullPath { get; private set; }
        public string SolutionDir => _pathProvider.GetPath(PathName.Solution);
        private static string GetProductName(string solutionName)
        {
            solutionName = GetSolutionNameNoPackages(GetSolutionNameNoExtension(solutionName));
            if (solutionName.Contains('.')) solutionName = solutionName.Substring(solutionName.IndexOf(".") + 1);
            return solutionName;
        }
        private static string GetCompanyName(string solutionName)
        {
            solutionName = GetSolutionNameNoPackages(GetSolutionNameNoExtension(solutionName));
            if(solutionName.Contains('.')) solutionName = solutionName.Substring(0, solutionName.IndexOf("."));
            return solutionName;
        }
        public static string GetProjectNameNoExtension(string projectName) =>
            projectName.EndsWith(projectExtension, StringComparison.InvariantCultureIgnoreCase) ? projectName.Substring(0, projectName.LastIndexOf(projectExtension, StringComparison.InvariantCultureIgnoreCase)) : projectName;
        public static string GetSolutionNameNoExtension(string solutionName) =>
            solutionName.EndsWith(solutionExtension, StringComparison.InvariantCultureIgnoreCase) ? solutionName.Substring(0, solutionName.LastIndexOf(solutionExtension, StringComparison.InvariantCultureIgnoreCase)) : solutionName;
        public static string GetSolutionNameNoPackages(string solutionName) =>
            solutionName.StartsWith(packagePrefix, StringComparison.InvariantCultureIgnoreCase) ? solutionName.Substring(solutionName.IndexOf(packagePrefix, StringComparison.InvariantCultureIgnoreCase) + packagePrefix.Length) : solutionName; 
        private static string GetComponentName(string classLibraryName) => classLibraryName.Contains(".") ? classLibraryName.Substring(classLibraryName.LastIndexOf(".") + 1) : classLibraryName;
        private static string GetDefaultStateMachineName(string classLibraryName) => $"{GetComponentName(classLibraryName)}StateMachine";

        private MonikerSubstitution CreateMonikers(string solutionName, string projectName)
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
        //Generate Solution
        public SolutionsGenerator(string repoDir, string solutionFileName)
        {
            var solutionName = GetSolutionNameNoExtension(solutionFileName);
            var projectName = GetSolutionNameNoPackages(solutionName);
            var monikers = CreateMonikers(solutionName, projectName);
            _pathProvider = new PathHelper(Path.Combine(repoDir, solutionName));
            _helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            GenerateSolutionFiles();
            GenerateProjectFiles();
            SolutionFullPath = Path.Combine(_pathProvider.GetPath(PathName.Solution), solutionFileName);
        }
        //Generate Project
        public SolutionsGenerator(string solutionDir, string solutionFileName, string projectName)
        {
            var solutionName = GetSolutionNameNoExtension(solutionFileName);
            var monikers = CreateMonikers(solutionName, projectName);
            _pathProvider = new PathHelper(solutionDir);
            _helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            GenerateProjectFiles();
            SolutionFullPath = Path.Combine(_pathProvider.GetPath(PathName.Solution), solutionFileName);
        }
        //Generate Proxy
        public SolutionsGenerator(string solutionDir, string solutionFileName, string projectDir, string projectFileName, string moniker)
        {
            var dialog = new OpenFileDialog();
            dialog.Title = "Select Class Library To Create Proxy To";
            dialog.Filter = "dll files (*.dll)|*.dll|All files (*.*)|*.*";
            dialog.Multiselect = false;
            var solutionName = GetSolutionNameNoExtension(solutionFileName);
            var projectName = GetProjectNameNoExtension(projectFileName);
            _pathProvider = new PathHelper(solutionDir, projectDir, projectName);
            SolutionFullPath = Path.Combine(_pathProvider.GetPath(PathName.Solution), solutionFileName);
            var monikers = CreateMonikers(solutionName, projectFileName);
            _helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            string serviceBinDirectory = _pathProvider.GetPath(PathName.Bin);
            if (Directory.Exists(serviceBinDirectory))
            {
                dialog.InitialDirectory = serviceBinDirectory;
                dialog.RestoreDirectory = true;
            }
            string dllFileName = string.Empty;
            if (DialogResult.OK == dialog.ShowDialog())
            {
                dllFileName = dialog.FileName;
                Console.WriteLine($"dll file: {dllFileName}");
                ProxyGenerator proxyCreator = new(dllFileName, projectName, moniker, _pathProvider);
                if (!Directory.Exists(_pathProvider.GetPath(PathName.Proxies))) Directory.CreateDirectory(_pathProvider.GetPath(PathName.Proxies));
                string outputFile = Path.Combine(_pathProvider.GetPath(PathName.Proxies), $"{moniker}Proxy.cs");
                bool outputFileAlreadExists = File.Exists(outputFile);
                proxyCreator.SaveToFile(outputFile);
                if (outputFileAlreadExists) return;
                monikers.AddMoniker("@#$ProxyCoreLibTopLevelNamespace@#$", proxyCreator.DefaultConfigNamespace);
                monikers.AddMoniker("@#$ProxyName@#$", moniker);
                GenerateProxyFiles();
            }
            else
            {
                Console.WriteLine("Action canceled.");
                return;
            }
        }
        private void GenerateSolutionFiles()
        {
            _helper.MoveToRootDirectory();
            //Solution Level
            _helper.SaveBinaryFile("SetupAndRunInstructions_pdf.sample", "SetupAndRunInstructions.pdf");
            _helper.SaveTextFile("SolutionInfo_proj.sample", "SolutionInfo.proj");
            _helper.SaveTextFile("_dockerignore.sample", ".dockerignore");
            _helper.SaveTextFile("_gitignore.sample", ".gitignore");
            _helper.SaveTextFile("Solution_sln.sample", $"{solutionNameMoniker}.sln");
            _helper.MoveTo("RunScript");
            _helper.SaveTextFile("DockerInfrastructureStart_ps1.sample", "DockerInfrastructureStart.ps1");
            _helper.SaveTextFile("DockerInfrastructureStop_ps1.sample", "DockerInfrastructureStop.ps1");
            _helper.SaveTextFile("Start_ps1.sample", "Start.ps1");
            _helper.SaveTextFile("Stop_ps1.sample", "Stop.ps1");
            _helper.MoveUp();
            _helper.MoveTo("BuildScript");
            _helper.SaveTextFile("NugetConfig_xml.sample", "NugetConfig.xml");
        }
        private void GenerateProjectFiles()
        {
            _helper.MoveToRootDirectory();
            _helper.Inject("SolutionProjectInjectionPoint1_sln.sample", $"{solutionNameMoniker}.sln", solutionInjection1Moniker);
            _helper.Inject("SolutionProjectInjectionPoint2_sln.sample", $"{solutionNameMoniker}.sln", solutionInjection2Moniker);
            _helper.MoveToRootDirectory();
            //Solution Level
            _helper.SaveTextFile("Tests_runsettings.sample", "Tests.runsettings");
            _helper.MoveTo("RunScript");
            _helper.SaveTextFile("DockerStart_ps1.sample", "@#$ClassLibraryName@#$.Service.DockerStart.ps1");
            _helper.SaveTextFile("DockerStop_ps1.sample", "@#$ClassLibraryName@#$.Service.DockerStop.ps1");
            _helper.Inject("ServiceStart_ps1.sample", "Start.ps1", solutionInjection1Moniker);
            _helper.Inject("ServiceStop_ps1.sample", "Stop.ps1", solutionInjection1Moniker);
            _helper.MoveUp();
            _helper.MoveTo("BuildScript");
            _helper.SaveTextFile("LocalImageBuild_ps1.sample", "@#$ClassLibraryName@#$.Service.LocalImageBuild.ps1");
            //Class library
            _helper.MoveToRootDirectory();
            _helper.MoveTo("@#$ClassLibraryName@#$");
            _helper.SaveTextFile("ClassLibrary_csproj.sample", "@#$ClassLibraryName@#$.csproj");
            _helper.MoveTo("Docs");
            _helper.SaveTextFile("ReadMe_md.sample", "ReadMe.md");
            _helper.MoveUp();
            _helper.MoveTo("Builders");
            _helper.SaveTextFile("DefaultSetup_cs.sample", "DefaultSetup.cs");
            _helper.SaveTextFile("DefaultServiceConfiguration_cs.sample", "DefaultServiceConfiguration.cs");
            _helper.MoveUp();
            _helper.MoveTo("Events");
            _helper.SaveTextFile("DummyEvent_cs.sample", "DummyEvent.cs");
            _helper.SaveTextFile("CurrentStatusEvent_cs.sample", "CurrentStatusEvent.cs");
            _helper.MoveUp();
            _helper.MoveTo("ValueObjects");
            _helper.SaveTextFile("ProxyMonikers_cs.sample", "ProxyMonikers.cs");
            _helper.MoveUp();
            _helper.MoveTo("StateMachines");
            _helper.MoveTo("@#$StateMachineName@#$");
            _helper.SaveTextFile("StateMachine_cs.sample", "@#$StateMachineName@#$.cs");
            _helper.MoveTo("States");
            _helper.SaveTextFile("ParentState_cs.sample", "ParentState.cs");
            _helper.MoveUp();
            _helper.MoveTo("Triggers");
            _helper.SaveTextFile("DummyTrigger_cs.sample", "DummyTrigger.cs");
            _helper.SaveTextFile("SendCurrentStatusCommand_cs.sample", "SendCurrentStatusCommand.cs");
            _helper.MoveTo("Internal");
            _helper.SaveTextFile("ProxyConnectionStatusTrigger_cs.sample", "ProxyConnectionStatusTrigger.cs");
            // Service
            _helper.MoveToRootDirectory();
            _helper.MoveTo("@#$ClassLibraryName@#$.Service");
            _helper.SaveTextFile("appsettings_Development_json.sample", "appsettings.Development.json");
            _helper.SaveTextFile("appsettings_json.sample", "appsettings.json");
            _helper.SaveTextFile("Dockerfile.sample", "Dockerfile");
            _helper.SaveTextFile("Service_csproj.sample", "@#$ClassLibraryName@#$.Service.csproj");
            _helper.SaveTextFile("Program_cs.sample", "Program.cs");
            _helper.MoveTo("Properties");
            _helper.SaveTextFile("launchSettings_json.sample", "launchSettings.json");
            // Test
            _helper.MoveToRootDirectory();
            _helper.MoveTo("@#$ClassLibraryName@#$.Test");
            _helper.SaveTextFile("Test_csproj.sample", "@#$ClassLibraryName@#$.Test.csproj");
            _helper.SaveTextFile("DummyDependencyRegistration_cs.sample", "DummyDependencyRegistration.cs");
            _helper.SaveTextFile("StateMachineDummyTests_cs.sample", "StateMachineDummyTests.cs");
            _helper.SaveTextFile("TestCategories_cs.sample", "TestCategories.cs");
        }
        private void GenerateProxyFiles()
        {
            //Class library
            _helper.MoveToRootDirectory();
            _helper.MoveTo("@#$ClassLibraryName@#$");
            _helper.MoveTo("Builders");
            _helper.Inject("DefaultSetup_Register.sample", $"DefaultSetup.cs", proxyInjection1Moniker);
            _helper.Inject("DefaultSetup_Build.sample", $"DefaultSetup.cs", proxyInjection2Moniker);
            _helper.Inject("DefaultServiceConfiguration_AddProxy.sample", $"DefaultServiceConfiguration.cs", proxyInjection4Moniker);
            _helper.MoveUp();
            _helper.MoveTo("ValueObjects");
            _helper.Inject("ProxyMonikers_Moniker.sample", $"ProxyMonikers.cs", proxyInjection1Moniker);
        }
    }
}
