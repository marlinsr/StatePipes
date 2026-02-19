namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        private const string proxyFileNamePostFix = "Proxy.cs";
        public void GenerateProxy(string projectDir, string projectFileName, string targetDirectory, string? updateMonikerFilePath)
        {
            var moniker = GetProxyMoniker(updateMonikerFilePath);
            if(string.IsNullOrEmpty(moniker)) return;
            var projectName = GetProjectNameNoExtension(projectFileName);
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectFileName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            string serviceBinDirectory = _pathProvider.GetPath(PathName.Bin);
            var dllFileName = SelectionDialog.ShowDllSelection(serviceBinDirectory);
            if (string.IsNullOrEmpty(dllFileName)) return;
            ProxyGenerator proxyCreator = new(dllFileName, projectName, moniker, _pathProvider);
            if (!Directory.Exists(_pathProvider.GetPath(PathName.Proxies))) Directory.CreateDirectory(_pathProvider.GetPath(PathName.Proxies));
            string outputFile = Path.Combine(_pathProvider.GetPath(PathName.Proxies), $"{moniker}{proxyFileNamePostFix}");
            bool outputFileAlreadExists = File.Exists(outputFile);
            proxyCreator.SaveToFile(outputFile);
            if (outputFileAlreadExists) return;
            monikers.AddMoniker("@#$ProxyName@#$", moniker);
            GenerateProxyFiles(helper);
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
        public static void CreateNewProxy(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, string? updateMonikerFilePath)
        {
            if (!IsServiceProject(projectFileName)) return;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            (new ProxyGeneratorTool(solutionDir, solutionFileName)).GenerateProxy(projDir, projectName, targetDirectory, updateMonikerFilePath);
        }
        private static string GetProxyMoniker(string? updateMonikerFilePath)
        {
            if (!string.IsNullOrEmpty(updateMonikerFilePath))
            {
                var fileName = Path.GetFileName(updateMonikerFilePath)!;
                var postFixIndex = fileName.IndexOf(proxyFileNamePostFix);
                if(postFixIndex > 0) return Path.GetFileName(updateMonikerFilePath)[..postFixIndex];
                System.Console.WriteLine($"The selected file {fileName} does not have the expected format of [Moniker]{proxyFileNamePostFix}");
                return string.Empty;
            }
            string proxyMoniker = "";
            if (SelectionDialog.ShowInputDialog(ref proxyMoniker, $"Enter the moniker for the proxy") == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(proxyMoniker))
                {
                    Console.WriteLine($"Bad Moniker>: {proxyMoniker}");
                    return string.Empty;
                }
            }
            return proxyMoniker;
        }
    }
}
