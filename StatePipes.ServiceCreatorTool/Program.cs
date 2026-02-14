using System.Net;

namespace StatePipes.ServiceCreatorTool
{
    public class Program
    {
        static string solutionDir = string.Empty;
        static string solutionFileName = string.Empty;
        static string projectFileName = string.Empty;
        static string targetDirectory = string.Empty;
        static bool addStateMachine = false;
        static bool addTrigger = false;
        private static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("-r", StringComparison.CurrentCultureIgnoreCase)) solutionDir = args[++i];
                if (args[i].Equals("-s", StringComparison.CurrentCultureIgnoreCase)) solutionFileName = args[++i];
                if (args[i].Equals("-p", StringComparison.CurrentCultureIgnoreCase)) projectFileName = args[++i];
                if (args[i].Equals("-b", StringComparison.CurrentCultureIgnoreCase)) targetDirectory = args[++i];
                if (args[i].Equals("-m", StringComparison.CurrentCultureIgnoreCase)) addStateMachine = true;
                if (args[i].Equals("-t", StringComparison.CurrentCultureIgnoreCase)) addTrigger = true;
            }
        }
        private static void ParameterErrors()
        {
            Console.WriteLine("Incorrect Parameters Specified!");
            var repoDirStr = string.IsNullOrEmpty(solutionDir) ? "null" : solutionDir;
            Console.WriteLine($"repoDir {repoDirStr}");
            var solutionFileNameStr = string.IsNullOrEmpty(solutionFileName) ? "null" : solutionFileName;
            Console.WriteLine($"solutionFileName {solutionFileNameStr}");
            var projectNameStr = string.IsNullOrEmpty(projectFileName) ? "null" : projectFileName;
            Console.WriteLine($"projectName {projectNameStr}");
        }
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                ParseArgs(args);
                if (DetermineCreation())
                {
                    Console.WriteLine("Finished!!!");
                    return;
                }
                ParameterErrors();
            }
            catch (Exception e) { Console.WriteLine($"Aborting Exception: {e.Message}"); }
        }
        private static bool DetermineCreation()
        {
            if (string.IsNullOrEmpty(solutionFileName) && string.IsNullOrEmpty(solutionDir) && string.IsNullOrEmpty(projectFileName))
            {
                CreateNewSolution();
                return true;
            }
            if (!string.IsNullOrEmpty(solutionFileName) && !string.IsNullOrEmpty(solutionDir) && string.IsNullOrEmpty(projectFileName))
            {
                CreateNewProjects(solutionDir, solutionFileName);
                return true;
            }
            if (!string.IsNullOrEmpty(solutionFileName) && !string.IsNullOrEmpty(solutionDir) && !string.IsNullOrEmpty(projectFileName) && addTrigger)
            {
                CreateNewTrigger();
                return true;
            }
            if (!string.IsNullOrEmpty(solutionFileName) && !string.IsNullOrEmpty(solutionDir) && !string.IsNullOrEmpty(projectFileName) && addStateMachine)
            {
                CreateNewStateMachine();
                return true;
            }
            if (!string.IsNullOrEmpty(solutionFileName) && !string.IsNullOrEmpty(solutionDir) && !string.IsNullOrEmpty(projectFileName))
            {
                CreateNewProxy();
                return true;
            }
            return false;
        }
        private static void CreateNewProxy()
        {
            var projectName = SolutionsGenerator.GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            if (projectFileName.EndsWith(".Service.csproj"))
            {
                Console.WriteLine("Service project highlighted, need to be in the class library!");
                return;
            }
            AddNewProxy(solutionDir, solutionFileName, projDir, projectName);
        }
        private static void AddNewProxy(string solutionDir, string solutionFileName, string projectDir, string projectName)
        {
            string answer = "";
            if (SelectionDialog.ShowInputDialog(ref answer, $"Enter the moniker for the proxy") == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(answer))
                {
                    Console.WriteLine($"Bad Moniker>: {answer}");
                    return;
                }
                _ = new SolutionsGenerator(solutionDir, solutionFileName, projectDir, projectName, targetDirectory, answer);
            }
        }
        private static void CreateNewStateMachine()
        {
            var projectName = SolutionsGenerator.GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            if (projectFileName.EndsWith(".Service.csproj"))
            {
                Console.WriteLine("Service project highlighted, need to be in the class library!");
                return;
            }
            AddNewStateMachine(solutionDir, solutionFileName, projDir, projectName, targetDirectory);
        }
        private static void AddNewStateMachine(string solutionDir, string solutionFileName, string projectDir, string projectName, string configuration)
        {
            string answer = "";
            if (SelectionDialog.ShowInputDialog(ref answer, "Enter the name for the new state machine") == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(answer))
                {
                    Console.WriteLine($"Bad state machine name: {answer}");
                    return;
                }
                _ = new SolutionsGenerator(solutionDir, solutionFileName, projectDir, projectName, configuration, answer, isStateMachine: true);
            }
        }
        private static void CreateNewTrigger()
        {
            var projectName = SolutionsGenerator.GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            if (projectFileName.EndsWith(".Service.csproj"))
            {
                Console.WriteLine("Service project highlighted, need to be in the class library!");
                return;
            }
            var selectedStateMachine = StateMachineSelection.GetStateMachineName(projectName, new PathHelper(solutionDir, projDir, projectName, targetDirectory))?.Name;
            if (selectedStateMachine == null)
            {
                Console.WriteLine("Action canceled.");
                return;
            }
            var scopeOptions = new List<string> { "Public", "Internal" };
            var selectedScope = SelectionDialog.ShowListSelection(scopeOptions, "Select the trigger scope");
            if (selectedScope == null)
            {
                Console.WriteLine("Action canceled.");
                return;
            }
            bool isInternal = selectedScope == "Internal";
            string answer = "";
            if (SelectionDialog.ShowInputDialog(ref answer, $"Enter the name for the new trigger on {selectedStateMachine}") == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(answer))
                {
                    Console.WriteLine($"Bad trigger name: {answer}");
                    return;
                }
                _ = new SolutionsGenerator(solutionDir, solutionFileName, projDir, projectName, targetDirectory, selectedStateMachine, answer, isInternal);
            }
        }
        private static void CreateNewProjects(string solutionDir, string solutionFileName)
        {
            var solutionName = SolutionsGenerator.GetSolutionNameNoExtension(solutionFileName);
            var projectName = SolutionsGenerator.GetSolutionNameNoPackages(solutionName);
            var defaultAnswer = $"{projectName}.";
            string answer = defaultAnswer;
            if (SelectionDialog.ShowInputDialog(ref answer, $"Enter the name of the project prefix for the service (no extension), ie: {projectName}.Vhw") == DialogResult.OK)
            {
                if (answer == defaultAnswer || string.IsNullOrEmpty(answer) || answer.TrimEnd().EndsWith('.'))
                {
                    Console.WriteLine($"Bad project name: {answer}");
                    return;
                }
                _ = new SolutionsGenerator(solutionDir, solutionFileName, answer);
            }
        }
        private static void CreateNewSolution()
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
                if (answer.EndsWith(SolutionsGenerator.solutionExtension) == false)
                {
                    answer += SolutionsGenerator.solutionExtension;
                }
                SolutionsGenerator sc = new(repoDirectory, answer);
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
            if(showReminder) MessageBox.Show(hostsText);
            return showReminder;
        }
 
    }
}
