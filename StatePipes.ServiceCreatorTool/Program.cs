using System.Net;

namespace StatePipes.ServiceCreatorTool
{
    public class Program
    {
        static string solutionDir = string.Empty;
        static string solutionFileName = string.Empty;
        static string projectFileName = string.Empty;
        private static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].ToLower() == "-r") solutionDir = args[++i];
                if (args[i].ToLower() == "-s") solutionFileName = args[++i];
                if (args[i].ToLower() == "-p") projectFileName = args[++i];
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
            var solutionName = SolutionsGenerator.GetSolutionNameNoExtension(solutionFileName);
            string answer = "";
            if (ShowInputDialog(ref answer, $"Enter the moniker for the proxy") == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(answer))
                {
                    Console.WriteLine($"Bad Moniker>: {answer}");
                    return;
                }
                SolutionsGenerator sc = new(solutionDir, solutionFileName, projectDir, projectName, answer);
            }
        }
        private static void CreateNewProjects(string solutionDir, string solutionFileName)
        {
            var solutionName = SolutionsGenerator.GetSolutionNameNoExtension(solutionFileName);
            var projectName = SolutionsGenerator.GetSolutionNameNoPackages(solutionName);
            var defaultAnswer = $"{projectName}.";
            string answer = defaultAnswer;
            if (ShowInputDialog(ref answer, $"Enter the name of the project prefix for the service (no extension), ie: {projectName}.Vhw") == DialogResult.OK)
            {
                if (answer == defaultAnswer || string.IsNullOrEmpty(answer) || answer.TrimEnd().EndsWith("."))
                {
                    Console.WriteLine($"Bad project name: {answer}");
                    return;
                }
                SolutionsGenerator sc = new(solutionDir, solutionFileName, answer);
            }
        }
        private static void CreateNewSolution()
        {
            var dialog = new FolderBrowserDialog();
            dialog.Description = "Repo root directory to place new solution folder";
            string repoDirectory = string.Empty;
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
            if (ShowInputDialog(ref answer, "Enter the name of the solution directory [Prefix of 'Packages.' will be omitted in the solution name]") == DialogResult.OK)
            {
                if (answer == defaultAnswer || string.IsNullOrEmpty(answer) || answer.TrimEnd().EndsWith("."))
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
        private static DialogResult ShowInputDialog(ref string input, string question)
        {
            Size size = new(800, 70);
            Form inputBox = new();
            inputBox.FormBorderStyle = FormBorderStyle.FixedDialog;
            inputBox.ClientSize = size;
            inputBox.Text = question;
            TextBox textBox = new();
            textBox.Size = new Size(size.Width - 10, 23);
            textBox.Location = new Point(5, 5);
            textBox.Text = input;
            inputBox.Controls.Add(textBox);
            Button okButton = new();
            okButton.DialogResult = DialogResult.OK;
            okButton.Name = "okButton";
            okButton.Size = new Size(75, 23);
            okButton.Text = "&OK";
            okButton.Location = new Point(size.Width - 80 - 80, 39);
            inputBox.Controls.Add(okButton);
            Button cancelButton = new();
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(75, 23);
            cancelButton.Text = "&Cancel";
            cancelButton.Location = new Point(size.Width - 80, 39);
            inputBox.Controls.Add(cancelButton);
            inputBox.AcceptButton = okButton;
            inputBox.CancelButton = cancelButton;
            DialogResult result = inputBox.ShowDialog();
            input = textBox.Text;
            return result;
        }
    }
}
