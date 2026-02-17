using System.Net;
namespace StatePipes.ServiceCreatorTool
{
    internal class SolutionGeneratorTool : BaseToolGenerator
    {
        public SolutionGeneratorTool(string solutionDir, string solutionFileName) : base(solutionDir, solutionFileName) {}
        public void GenerateSolution()
        {
            var projectName = GetSolutionNameNoPackages(SolutionNameNoExtension);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            GenerateSolutionFiles(helper);
            ProjectGeneratorTool.GenerateProjectFiles(helper);
        }
        public static void GenerateSolutionFiles(GeneratorHelper helper)
        {
            helper.MoveToRootDirectory();
            //Solution Level
            helper.SaveBinaryFile("SetupAndRunInstructions_pdf.sample", "SetupAndRunInstructions.pdf");
            helper.SaveTextFile("SolutionInfo_proj.sample", "SolutionInfo.proj");
            helper.SaveTextFile("_dockerignore.sample", ".dockerignore");
            helper.SaveTextFile("_gitignore.sample", ".gitignore");
            helper.SaveTextFile("Solution_sln.sample", $"{solutionNameMoniker}.sln");
            helper.MoveTo("RunScript");
            helper.SaveTextFile("DockerInfrastructureStart_ps1.sample", "DockerInfrastructureStart.ps1");
            helper.SaveTextFile("DockerInfrastructureStop_ps1.sample", "DockerInfrastructureStop.ps1");
            helper.SaveTextFile("Start_ps1.sample", "Start.ps1");
            helper.SaveTextFile("Stop_ps1.sample", "Stop.ps1");
            helper.MoveUp();
            helper.MoveTo("BuildScript");
            helper.SaveTextFile("NugetConfig_xml.sample", "NugetConfig.xml");
        }
        public static void CreateNewSolution()
        {
            FolderBrowserDialog dialog = new()
            {
                Description = "Repo root directory to place new solution folder"
            };
            string? repoDirectory;
            if (DialogResult.OK == dialog.ShowDialog())
            {
                repoDirectory = dialog.SelectedPath;
            }
            else
            {
                Console.WriteLine("Action canceled.");
                return;
            }
            if (string.IsNullOrEmpty(repoDirectory))
            {
                Console.WriteLine("Empty path to repo root directory! Terminating.");
                return;
            }
            CreateNewSolutionAndLaunch(repoDirectory);
        }
        private static void CreateNewSolutionAndLaunch(string repoDirectory)
        {
            const string defaultAnswer = "Packages.MyCompany.MyProduct";
            string answer = defaultAnswer;
            if (SelectionDialog.ShowInputDialog(ref answer, "Enter the name of the solution directory [Prefix of 'Packages.' will be omitted in the solution name]") == DialogResult.OK)
            {
                if (answer == defaultAnswer || string.IsNullOrEmpty(answer) || answer.TrimEnd().EndsWith('.'))
                {
                    Console.WriteLine($"Bad solution name: {answer}");
                    return;
                }
                if (answer.EndsWith(solutionExtension) == false)
                {
                    answer += solutionExtension;
                }
                var sc = new SolutionGeneratorTool(Path.Combine(repoDirectory, BaseToolGenerator.GetSolutionNameNoExtension(answer)), answer);
                sc.GenerateSolution();
                bool missingHostFileEntries = HostFileReminder();
                if (missingHostFileEntries) PdfOpener.OpenPdfFile(Path.Combine(sc.SolutionDir, "SetupAndRunInstructions.pdf"));
                VisualStudioLauncher.LaunchSolution(sc.SolutionFullPath);
            }
        }
        private static bool HostFileReminder()
        {
            var statePipesHostName = "statepipes.explorer";
            var stepCaHostName = "step-ca";
            var brokerHostName = "amqp09-broker";
            bool showReminder = false;
            try
            {
                Dns.GetHostAddresses(statePipesHostName);
                Dns.GetHostAddresses(stepCaHostName);
                Dns.GetHostAddresses(brokerHostName);
            }
            catch { showReminder = true; }
            var hostsText = "Please add to your etc\\hosts file the following entires before proceeding:\n" +
                $"\t127.0.0.1 {statePipesHostName}\n" +
                $"\t127.0.0.1 {stepCaHostName}\n" +
                $"\t127.0.0.1 {brokerHostName}\n";
            if (showReminder) MessageBox.Show(hostsText);
            return showReminder;
        }
    }
}
