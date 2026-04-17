namespace StatePipes.ServiceCreatorTool
{
    internal class LiveProxyGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        private const string ProxyFileNamePostFix = "Proxy.cs";
        private string _brokerUri = string.Empty;
        private string _exchangeName = string.Empty;
        private string _certPath = string.Empty;
        private string _certPasswordPath = string.Empty;
        private bool GetCommsParamsFromUser()
        {
            _brokerUri = "amqps://amqp09-broker/Production";
            _exchangeName = string.Empty;
            if (!SelectionDialog.GetUserInput(ref _brokerUri, $"Enter Broker URI")) return false;
            if (!SelectionDialog.GetUserInput(ref _exchangeName, $"Enter Exchange Name")) return false;
            if (!SelectionDialog.SelectFile(out _certPath, "Select Certificate File", "p12 files (*.p12)|*.p12|All files (*.*)|*.*")) return false;
            if (!SelectionDialog.SelectFile(out _certPasswordPath, "Select Password File", "txt files (*.txt)|*.txt|All files (*.*)|*.*")) return false;
            return true;
        }
        private bool GetTypeList(out TypeSerializationList? typeList, int timeoutSeconds)
        {
            if (!GetCommsParamsFromUser())
            {
                typeList = null;
                return false;
            }
            Console.WriteLine($"Connecting to {_brokerUri} ({_exchangeName})...");
            typeList = new LiveServiceDescriptionClient(_brokerUri, _exchangeName, _certPath, _certPasswordPath).Fetch(timeoutSeconds);
            if (typeList == null) Console.Error.WriteLine("Failed to retrieve self-description from the running service.");
            return !(typeList == null);
        }
        public string GenerateProxy(
            string projDir, string projectFileName, string targetDirectory,
            int timeoutSeconds, string? updateMonikerFilePath)
        {
            var moniker = GetProxyMoniker(updateMonikerFilePath);
            if (string.IsNullOrEmpty(moniker)) return string.Empty;
            var projectName = GetProjectNameNoExtension(projectFileName);
            _pathProvider.AddPaths(projDir, projectName, targetDirectory);
            if(!GetTypeList(out TypeSerializationList? typeList, timeoutSeconds)) return string.Empty;          
            var proxyCreator = new ProxyGenerator(typeList!, projectName, moniker, _pathProvider, _brokerUri, _exchangeName, _certPath, _certPasswordPath);
            if (!Directory.Exists(_pathProvider.GetPath(PathName.Proxies))) Directory.CreateDirectory(_pathProvider.GetPath(PathName.Proxies));
            string outputFile = Path.Combine(_pathProvider.GetPath(PathName.Proxies), $"{moniker}{ProxyFileNamePostFix}");
            bool outputFileAlreadyExists = File.Exists(outputFile);
            proxyCreator.SaveToFile(outputFile);
            Console.WriteLine($"Proxy written to: {outputFile}");
            if (outputFileAlreadyExists) return string.Empty;
            var monikers = CreateMonikers(SolutionNameNoExtension, projectFileName);
            monikers.AddMoniker("@#$ProxyName@#$", moniker);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            ProxyGeneratorTool.GenerateProxyFiles(helper);
            return outputFile;
        }
        public static string CreateNewLiveProxy(
            string solutionDir, string solutionFileName, string projectFileName, string targetDirectory,
            int timeoutSeconds, string? updateMonikerFilePath)
        {
            if (!IsServiceProject(projectFileName)) return string.Empty;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            return new LiveProxyGeneratorTool(solutionDir, solutionFileName).GenerateProxy(
                projDir, projectName, targetDirectory,
                timeoutSeconds, updateMonikerFilePath);
        }
        private static string GetProxyMoniker(string? updateMonikerFilePath)
        {
            if (!string.IsNullOrEmpty(updateMonikerFilePath))
            {
                var fileName = Path.GetFileName(updateMonikerFilePath)!;
                var postFixIndex = fileName.IndexOf(ProxyFileNamePostFix);
                if (postFixIndex > 0) return Path.GetFileName(updateMonikerFilePath)[..postFixIndex];
                System.Console.WriteLine($"The selected file {fileName} does not have the expected format of [Moniker]{ProxyFileNamePostFix}");
                return string.Empty;
            }
            string proxyMoniker = "";
            if (!SelectionDialog.GetUserInput(ref proxyMoniker, $"Enter the moniker for the proxy")) return string.Empty;
            return proxyMoniker;
        }
    }
}
