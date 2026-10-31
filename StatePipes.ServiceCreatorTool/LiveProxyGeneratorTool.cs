namespace StatePipes.ServiceCreatorTool
{
    internal class LiveProxyGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        private const string ProxyFileNamePostFix = "Proxy.cs";
        private string _brokerUri = string.Empty;
        private string _exchangeName = string.Empty;
        private string _certPath = string.Empty;
        private string _certPasswordPath = string.Empty;
        private bool GetCommsParamsFromUser(bool useRabbitMQ, IToolInput input)
        {
            var brokerUri = input.Text("--broker", "Enter Broker URI", useRabbitMQ ? "amqps://amqp09-broker/Production" : "ssl://kafka-broker:9093");
            if (brokerUri == null) return false;
            var exchangeName = input.Text("--exchange", "Enter Exchange Name");
            if (exchangeName == null) return false;
            var certPath = input.File("--cert", "Select Certificate File", "p12 files (*.p12)|*.p12|All files (*.*)|*.*");
            if (certPath == null) return false;
            var certPasswordPath = input.File("--certpw", "Select Password File", "txt files (*.txt)|*.txt|All files (*.*)|*.*");
            if (certPasswordPath == null) return false;
            (_brokerUri, _exchangeName, _certPath, _certPasswordPath) = (brokerUri, exchangeName, certPath, certPasswordPath);
            return true;
        }
        private bool GetTypeList(out TypeSerializationList? typeList, int timeoutSeconds, bool useRabbitMQ, IToolInput input)
        {
            if (!GetCommsParamsFromUser(useRabbitMQ, input))
            {
                typeList = null;
                return false;
            }
            Console.WriteLine($"Connecting to {_brokerUri} ({_exchangeName})...");
            typeList = new LiveServiceDescriptionClient(_brokerUri, _exchangeName, _certPath, _certPasswordPath).Fetch(timeoutSeconds);
            if (typeList == null) Console.Error.WriteLine("Failed to retrieve self-description from the running service.");
            return !(typeList == null);
        }
        public ToolResult GenerateProxy(
            string projDir, string projectFileName, string targetDirectory,
            int timeoutSeconds, string? updateMonikerFilePath, IToolInput input)
        {
            var moniker = ProxyGeneratorTool.GetProxyMoniker(updateMonikerFilePath, input);
            if (string.IsNullOrEmpty(moniker)) return ToolResult.Failed;
            var projectName = GetProjectNameNoExtension(projectFileName);
            _pathProvider.AddPaths(projDir, projectName, targetDirectory);
            var toolConfig = ToolConfigurationUtility.ReadConfiguration(_pathProvider.GetPath(PathName.Solution));
            if (!GetTypeList(out TypeSerializationList? typeList, timeoutSeconds, toolConfig?.UseRabbitMQ ?? true, input)) return ToolResult.Failed;
            var proxyCreator = new ProxyGenerator(typeList!, projectName, moniker, _pathProvider, _brokerUri, _exchangeName, _certPath, _certPasswordPath, ProxyGeneratorTool.WithStateMachineFromProxyFile(input, updateMonikerFilePath));
            if (!Directory.Exists(_pathProvider.GetPath(PathName.Proxies))) Directory.CreateDirectory(_pathProvider.GetPath(PathName.Proxies));
            string outputFile = Path.Combine(_pathProvider.GetPath(PathName.Proxies), $"{moniker}{ProxyFileNamePostFix}");
            bool outputFileAlreadyExists = File.Exists(outputFile);
            proxyCreator.SaveToFile(outputFile);
            EnsureProxyConnectionStatusTrigger(projectName, proxyCreator.StateMachineName);
            Console.WriteLine($"Proxy written to: {outputFile}");
            if (outputFileAlreadyExists) return ToolResult.Ok();
            var monikers = CreateMonikers(SolutionNameNoExtension, projectFileName, toolConfig?.UseRabbitMQ ?? true);
            monikers.AddMoniker("@#$ProxyName@#$", moniker);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            ProxyGeneratorTool.GenerateProxyFiles(helper);
            return ToolResult.Ok(outputFile);
        }
        public static ToolResult CreateNewLiveProxy(
            string solutionDir, string solutionFileName, string projectFileName, string targetDirectory,
            int timeoutSeconds, string? updateMonikerFilePath, IToolInput input)
        {
            if (!IsServiceProject(projectFileName)) return ToolResult.Failed;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            return new LiveProxyGeneratorTool(solutionDir, solutionFileName).GenerateProxy(
                projDir, projectName, targetDirectory,
                timeoutSeconds, updateMonikerFilePath, input);
        }
    }
}
