namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyGeneratorTool : BaseToolGenerator
    {
        public ProxyGeneratorTool(string solutionDir, string solutionFileName) : base(solutionDir, solutionFileName) { }
        public void GenerateProxy(string projectDir, string projectFileName, string targetDirectory, string moniker)
        {
            var projectName = GetProjectNameNoExtension(projectFileName);
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectFileName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            string serviceBinDirectory = _pathProvider.GetPath(PathName.Bin);
            var dllFileName = SelectionDialog.ShowDllSelection(serviceBinDirectory);
            if (string.IsNullOrEmpty(dllFileName)) return;
            ProxyGenerator proxyCreator = new(dllFileName, projectName, moniker, _pathProvider);
            if (!Directory.Exists(_pathProvider.GetPath(PathName.Proxies))) Directory.CreateDirectory(_pathProvider.GetPath(PathName.Proxies));
            string outputFile = Path.Combine(_pathProvider.GetPath(PathName.Proxies), $"{moniker}Proxy.cs");
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
        public static void CreateNewProxy(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory)
        {
            if (!IsServiceProject(projectFileName)) return;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            AddNewProxy(solutionDir, solutionFileName, projDir, projectName, targetDirectory);
        }
        private static void AddNewProxy(string solutionDir, string solutionFileName, string projectDir, string projectName, string targetDirectory)
        {
            string answer = "";
            if (SelectionDialog.ShowInputDialog(ref answer, $"Enter the moniker for the proxy") == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(answer))
                {
                    Console.WriteLine($"Bad Moniker>: {answer}");
                    return;
                }
                (new ProxyGeneratorTool(solutionDir, solutionFileName)).GenerateProxy(projectDir, projectName, targetDirectory, answer);
            }
        }
    }
}
