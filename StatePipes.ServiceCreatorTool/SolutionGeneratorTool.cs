using System.Net;
namespace StatePipes.ServiceCreatorTool
{
    internal class SolutionGeneratorTool(string solutionDir, string solutionFileName, bool usingRabbit) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        public void GenerateSolution()
        {
            var projectName = GetSolutionNameNoPackages(SolutionNameNoExtension);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName, usingRabbit);
            var slnDir = _pathProvider.GetPath(PathName.Solution);
            var helper = new GeneratorHelper(new DirectoryHelper(slnDir), monikers);
            GenerateSolutionFiles(helper);
            ToolConfigurationUtility.WriteConfiguration(slnDir, new ToolConfiguration { UseRabbitMQ = usingRabbit });
            ProjectGeneratorTool.GenerateProjectFiles(helper);
        }
        public static void GenerateSolutionFiles(GeneratorHelper helper)
        {
            helper.MoveToRootDirectory();
            //Solution Level
            helper.SaveBinaryFile("SetupAndRunInstructions_pdf.sample", "SetupAndRunInstructions.pdf");
            _ = helper.SaveTextFile("SolutionInfo_proj.sample", "SolutionInfo.proj");
            _ = helper.SaveTextFile("_dockerignore.sample", ".dockerignore");
            _ = helper.SaveTextFile("_gitignore.sample", ".gitignore");
            _ = helper.SaveTextFile("Solution_slnx.sample", $"{solutionNameMoniker}{solutionExtension}");
            helper.MoveTo("RunScript");
            _ = helper.SaveTextFile("DockerInfrastructureStart_ps1.sample", "DockerInfrastructureStart.ps1");
            _ = helper.SaveTextFile("DockerInfrastructureStop_ps1.sample", "DockerInfrastructureStop.ps1");
            _ = helper.SaveTextFile("Start_ps1.sample", "Start.ps1");
            _ = helper.SaveTextFile("Stop_ps1.sample", "Stop.ps1");
            helper.MoveUp();
            helper.MoveTo("BuildScript");
            _ = helper.SaveTextFile("NugetConfig_xml.sample", "NugetConfig.xml");
        }
        public static ToolResult CreateNewSolution(IToolInput input)
        {
            var repoDirectory = input.Folder("--repo", "Repo root directory to place new solution folder");
            if (repoDirectory == null) return ToolResult.Failed;
            var isRabbitMQ = input.Confirm("--transport", "rabbitmq", "kafka", "Are you using RabbitMQ? [select yes if RabbitMQ, select no if using Kafka]");
            if (isRabbitMQ == null) return ToolResult.Failed;
            const string defaultAnswer = "Packages.MyCompany.MyProduct";
            var answer = input.Text("--solution-name", "Enter the name of the solution directory [Prefix of 'Packages.' will be omitted in the solution name]", defaultAnswer);
            if (answer == null) return ToolResult.Failed;
            if (answer == defaultAnswer || answer.TrimEnd().EndsWith('.'))
            {
                Console.WriteLine($"Bad solution name: {answer}");
                return ToolResult.Failed;
            }
            if (answer.EndsWith(solutionExtension) == false)
            {
                answer += solutionExtension;
            }
            var solutionDir = Path.Combine(repoDirectory, BaseToolGenerator.GetSolutionNameNoExtension(answer));
            if (AlreadyExists(repoDirectory, solutionDir)) return ToolResult.Failed;
            var sc = new SolutionGeneratorTool(solutionDir, answer, isRabbitMQ.Value);
            sc.GenerateSolution();
            var hostsText = MissingHostEntries();
            if (input.Headless)
            {
                if (hostsText != null) Console.WriteLine(hostsText);
                Console.WriteLine($"Solution created: {sc.SolutionFullPath}");
                return ToolResult.Ok();
            }
            if (hostsText != null)
            {
                MessageBox.Show(hostsText);
                PdfOpener.OpenPdfFile(Path.Combine(sc.SolutionDir, "SetupAndRunInstructions.pdf"));
            }
            VisualStudioLauncher.LaunchSolution(sc.SolutionFullPath);
            return ToolResult.Ok();
        }
        /// <summary>The hosts file reminder, or null when every StatePipes host name resolves.</summary>
        private static string? MissingHostEntries()
        {
            var statePipesHostName = "statepipes.explorer";
            var stepCaHostName = "step-ca";
            var brokerHostName = "amqp09-broker";
            var kafkaHostName = "kafka-broker";
            var keycloakHostName = "keycloak";
            bool showReminder = false;
            try
            {
                Dns.GetHostAddresses(statePipesHostName);
                Dns.GetHostAddresses(stepCaHostName);
                Dns.GetHostAddresses(brokerHostName);
                Dns.GetHostAddresses(kafkaHostName);
                Dns.GetHostAddresses(keycloakHostName);
            }
            catch { showReminder = true; }
            var hostsText = "Please add to your etc\\hosts file the following entires before proceeding:\n" +
                $"\t127.0.0.1 {statePipesHostName}\n" +
                $"\t127.0.0.1 {stepCaHostName}\n" +
                $"\t127.0.0.1 {brokerHostName}\n" +
                $"\t127.0.0.1 {kafkaHostName}\n" +
                $"\t127.0.0.1 {keycloakHostName}\n";
            return showReminder ? hostsText : null;
        }
    }
}
