namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        private const string ProxyFileNamePostFix = "Proxy.cs";
        public ToolResult GenerateProxy(string projectDir, string projectFileName, string targetDirectory, string? updateMonikerFilePath, IToolInput input)
        {
            var moniker = GetProxyMoniker(updateMonikerFilePath, input);
            if (string.IsNullOrEmpty(moniker)) return ToolResult.Failed;
            var projectName = GetProjectNameNoExtension(projectFileName);
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectFileName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            string serviceBinDirectory = _pathProvider.GetPath(PathName.Bin);
            var dllFileName = input.File("--dll", "Select Class Library To Create Proxy To", "dll files (*.dll)|*.dll|All files (*.*)|*.*", serviceBinDirectory);
            if (dllFileName == null) return ToolResult.Failed;
            ProxyGenerator proxyCreator = new(dllFileName, projectName, moniker, _pathProvider, WithStateMachineFromProxyFile(input, updateMonikerFilePath));
            if (!Directory.Exists(_pathProvider.GetPath(PathName.Proxies))) Directory.CreateDirectory(_pathProvider.GetPath(PathName.Proxies));
            string outputFile = Path.Combine(_pathProvider.GetPath(PathName.Proxies), $"{moniker}{ProxyFileNamePostFix}");
            bool outputFileAlreadExists = File.Exists(outputFile);
            proxyCreator.SaveToFile(outputFile);
            EnsureProxyConnectionStatusTrigger(projectName, proxyCreator.StateMachineName);
            if (outputFileAlreadExists) return ToolResult.Ok();
            monikers.AddMoniker("@#$ProxyName@#$", moniker);
            GenerateProxyFiles(helper);
            return ToolResult.Ok(outputFile);
        }
        public static void GenerateProxyFiles(GeneratorHelper helper)
        {
            //Class library
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$");
            helper.MoveTo("Builders");
            helper.Inject("DefaultSetup_Register.sample", $"DefaultSetup.cs", proxyInjection1Moniker);
            helper.Inject("DefaultSetup_Build.sample", $"DefaultSetup.cs", proxyInjection2Moniker);
            helper.Inject("DefaultServiceConfiguration_AddProxy.sample", $"DefaultServiceConfiguration.cs", proxyInjection4Moniker);
            helper.MoveUp();
            helper.MoveTo("ValueObjects");
            helper.Inject("ProxyMonikers_Moniker.sample", $"ProxyMonikers.cs", proxyInjection1Moniker);
        }
        public static ToolResult CreateNewProxy(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, string? updateMonikerFilePath, IToolInput input)
        {
            if (!IsServiceProject(projectFileName)) return ToolResult.Failed;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            return (new ProxyGeneratorTool(solutionDir, solutionFileName)).GenerateProxy(projDir, projectName, targetDirectory, updateMonikerFilePath, input);
        }
        /// <summary>
        /// When no state machine was given, an update keeps the one the existing proxy's triggers already target.
        /// </summary>
        internal static IToolInput WithStateMachineFromProxyFile(IToolInput input, string? updateMonikerFilePath)
        {
            if (input.Supplied("--statemachine") || string.IsNullOrEmpty(updateMonikerFilePath) || !File.Exists(updateMonikerFilePath)) return input;
#pragma warning disable SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
            var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(updateMonikerFilePath), @"BaseTriggerCommand<([\w.]+)>");
#pragma warning restore SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
            if (!match.Success) return input;
            return input.WithDefault("--statemachine", match.Groups[1].Value.Split('.')[^1]);
        }
        internal static string? GetProxyMoniker(string? updateMonikerFilePath, IToolInput input)
        {
            if (!string.IsNullOrEmpty(updateMonikerFilePath))
            {
                var fileName = Path.GetFileName(updateMonikerFilePath)!;
                var postFixIndex = fileName.IndexOf(ProxyFileNamePostFix);
                if (postFixIndex > 0) return Path.GetFileName(updateMonikerFilePath)[..postFixIndex];
                System.Console.WriteLine($"The selected file {fileName} does not have the expected format of [Moniker]{ProxyFileNamePostFix}");
                return null;
            }
            return input.Name("--moniker", "Enter the moniker for the proxy");
        }
    }
}
