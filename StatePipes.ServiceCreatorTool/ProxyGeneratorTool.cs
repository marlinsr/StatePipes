namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        private const string ProxyFileNamePostFix = "Proxy.cs";
        public string GenerateProxy(string projectDir, string projectFileName, string targetDirectory, string? updateMonikerFilePath, ProxyInputs? inputs = null)
        {
            var moniker = GetProxyMoniker(updateMonikerFilePath, inputs);
            if (string.IsNullOrEmpty(moniker)) return string.Empty;
            var projectName = GetProjectNameNoExtension(projectFileName);
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectFileName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            string serviceBinDirectory = _pathProvider.GetPath(PathName.Bin);
            string? dllFileName = inputs?.DllPath;
            if (inputs?.MissingInHeadless(dllFileName, "--dll <path>") == true) return string.Empty;
            if (string.IsNullOrEmpty(dllFileName) && !SelectionDialog.SelectFile(out dllFileName, "Select Class Library To Create Proxy To", "dll files (*.dll)|*.dll|All files (*.*)|*.*", serviceBinDirectory)) return string.Empty;
            ProxyGenerator proxyCreator = new(dllFileName, projectName, moniker, _pathProvider, WithStateMachineFromProxyFile(inputs, updateMonikerFilePath));
            if (!Directory.Exists(_pathProvider.GetPath(PathName.Proxies))) Directory.CreateDirectory(_pathProvider.GetPath(PathName.Proxies));
            string outputFile = Path.Combine(_pathProvider.GetPath(PathName.Proxies), $"{moniker}{ProxyFileNamePostFix}");
            bool outputFileAlreadExists = File.Exists(outputFile);
            proxyCreator.SaveToFile(outputFile);
            EnsureProxyConnectionStatusTrigger(projectName, proxyCreator.StateMachineName);
            if (outputFileAlreadExists) return string.Empty;
            monikers.AddMoniker("@#$ProxyName@#$", moniker);
            GenerateProxyFiles(helper);
            return outputFile;
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
        public static string CreateNewProxy(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, string? updateMonikerFilePath, ProxyInputs? inputs = null)
        {
            if (!IsServiceProject(projectFileName)) return string.Empty;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            return (new ProxyGeneratorTool(solutionDir, solutionFileName)).GenerateProxy(projDir, projectName, targetDirectory, updateMonikerFilePath, inputs);
        }
        /// <summary>
        /// When no state machine was given, an update keeps the one the existing proxy's triggers already target.
        /// </summary>
        internal static ProxyInputs? WithStateMachineFromProxyFile(ProxyInputs? inputs, string? updateMonikerFilePath)
        {
            if (!string.IsNullOrEmpty(inputs?.StateMachine) || string.IsNullOrEmpty(updateMonikerFilePath) || !File.Exists(updateMonikerFilePath)) return inputs;
#pragma warning disable SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
            var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(updateMonikerFilePath), @"BaseTriggerCommand<([\w.]+)>");
#pragma warning restore SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
            if (!match.Success) return inputs;
            return (inputs ?? new ProxyInputs()) with { StateMachine = match.Groups[1].Value };
        }
        internal static string GetProxyMoniker(string? updateMonikerFilePath, ProxyInputs? inputs = null)
        {
            if (!string.IsNullOrEmpty(updateMonikerFilePath))
            {
                var fileName = Path.GetFileName(updateMonikerFilePath)!;
                var postFixIndex = fileName.IndexOf(ProxyFileNamePostFix);
                if (postFixIndex > 0) return Path.GetFileName(updateMonikerFilePath)[..postFixIndex];
                System.Console.WriteLine($"The selected file {fileName} does not have the expected format of [Moniker]{ProxyFileNamePostFix}");
                return string.Empty;
            }
            if (!string.IsNullOrEmpty(inputs?.Moniker)) return inputs.Moniker;
            if (inputs?.MissingInHeadless(inputs.Moniker, "--moniker <name> (or -u <proxy file>)") == true) return string.Empty;
            string proxyMoniker = "";
            if (!SelectionDialog.GetUserInput(ref proxyMoniker, $"Enter the moniker for the proxy")) return string.Empty;
            return proxyMoniker;
        }
    }
}
