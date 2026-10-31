using EnvDTE;
using EnvDTE80;
using System.Management;
using System.Runtime.InteropServices;

namespace StatePipes.ServiceCreatorTool
{
    public class Program
    {
        static string _solutionDir = string.Empty;
        static string _solutionFileName = string.Empty;
        static string _projectFileName = string.Empty;
        static string _targetDirectory = string.Empty;
        static bool _addStateMachine = false;
        static bool _addTrigger = false;
        static bool _addEvent = false;
        static bool _addState = false;
        static string _permitIfStateFilePath = string.Empty;
        static string _permitReentryIfStateFilePath = string.Empty;
        static string _ignoreIfStateFilePath = string.Empty;
        static string _periodicTriggerStateFilePath = string.Empty;
        static string _updateProxyFilePath = string.Empty;
        // Live proxy generation args
        static bool _liveMode = false;
        static int _liveTimeoutSeconds = 30;
        // Headless: no Visual Studio, and --name value arguments replace the dialogs
        static bool _headless = false;
        static Dictionary<string, string> _values = [];
        private static bool ParseArgs(string[] args)
        {
            _solutionDir = _solutionFileName = _projectFileName = _targetDirectory = string.Empty;
            _permitIfStateFilePath = _permitReentryIfStateFilePath = _ignoreIfStateFilePath = _periodicTriggerStateFilePath = _updateProxyFilePath = string.Empty;
            _addStateMachine = _addTrigger = _addEvent = _addState = _liveMode = _headless = false;
            _liveTimeoutSeconds = 30;
            var values = ToolArguments.Parse(args);
            if (values == null) return false;
            _values = values;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--")) { i++; continue; }
                if (args[i].Equals("-r", StringComparison.CurrentCultureIgnoreCase)) _solutionDir = args[++i];
                if (args[i].Equals("-s", StringComparison.CurrentCultureIgnoreCase)) _solutionFileName = args[++i];
                if (args[i].Equals("-p", StringComparison.CurrentCultureIgnoreCase)) _projectFileName = args[++i];
                if (args[i].Equals("-b", StringComparison.CurrentCultureIgnoreCase)) _targetDirectory = args[++i];
                if (args[i].Equals("-m", StringComparison.CurrentCultureIgnoreCase)) _addStateMachine = true;
                if (args[i].Equals("-t", StringComparison.CurrentCultureIgnoreCase)) _addTrigger = true;
                if (args[i].Equals("-e", StringComparison.CurrentCultureIgnoreCase)) _addEvent = true;
                if (args[i].Equals("-a", StringComparison.CurrentCultureIgnoreCase)) _addState = true;
                if (args[i].Equals("-pi", StringComparison.CurrentCultureIgnoreCase)) _permitIfStateFilePath = FullPath(args[++i]);
                if (args[i].Equals("-pri", StringComparison.CurrentCultureIgnoreCase)) _permitReentryIfStateFilePath = FullPath(args[++i]);
                if (args[i].Equals("-ii", StringComparison.CurrentCultureIgnoreCase)) _ignoreIfStateFilePath = FullPath(args[++i]);
                if (args[i].Equals("-pti", StringComparison.CurrentCultureIgnoreCase)) _periodicTriggerStateFilePath = FullPath(args[++i]);
                if (args[i].Equals("-u", StringComparison.CurrentCultureIgnoreCase)) _updateProxyFilePath = FullPath(args[++i]);
                if (args[i].Equals("-live", StringComparison.CurrentCultureIgnoreCase)) _liveMode = true;
                if (args[i].Equals("-headless", StringComparison.CurrentCultureIgnoreCase)) _headless = true;
            }
            if (_values.TryGetValue("--timeout", out var timeout) && int.TryParse(timeout, out var t)) _liveTimeoutSeconds = t;
            return true;
        }
        // State and proxy file paths may be relative to the current directory
        private static string FullPath(string path) => string.IsNullOrEmpty(path) ? path : Path.GetFullPath(path);
        private static void ParameterErrors()
        {
            Console.WriteLine("Incorrect Parameters Specified!");
            var repoDirStr = string.IsNullOrEmpty(_solutionDir) ? "null" : _solutionDir;
            Console.WriteLine($"repoDir {repoDirStr}");
            var solutionFileNameStr = string.IsNullOrEmpty(_solutionFileName) ? "null" : _solutionFileName;
            Console.WriteLine($"solutionFileName {solutionFileNameStr}");
            var projectNameStr = string.IsNullOrEmpty(_projectFileName) ? "null" : _projectFileName;
            Console.WriteLine($"projectName {projectNameStr}");
        }
        [STAThread]
        internal static int Main(string[] args)
        {
            try
            {
                if (!ParseArgs(args)) return 1;
                var result = DetermineCreation();
                if (result == null)
                {
                    ParameterErrors();
                    return 1;
                }
                if (!result.Succeeded) return 1;
                Console.WriteLine("Finished!!!");
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Aborting Exception: {e.Message}");
                return 1;
            }
        }
        private static int GetParentPid()
        {
            try
            {
                int currentPid = Environment.ProcessId;
                // Query WMI for the parent process ID associated with our current PID
                string query = $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {currentPid}";
                using ManagementObjectSearcher searcher = new(query);
                using var results = searcher.Get();
                foreach (var obj in results) return Convert.ToInt32(obj["ParentProcessId"]);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving parent PID: {ex.Message}");
            }
            return -1; // Return -1 if not found or error occurred
        }
        private static DTE2? GetDTE2()
        {
            try
            {
                int devEnvPID = GetParentPid();
                if (devEnvPID <= 0) return null;
                return ExternalDTE.GetDTE2(devEnvPID);
            }
            catch (COMException ex)
            {
                Console.WriteLine("Could not find a running Visual Studio instance: " + ex.Message);
                return null;
            }
        }
        private static bool SendSaveAndBuild(DTE2 dte)
        {
            bool buildSucceeded = true;
            CancellationTokenSource cts = new();
            dte.Events.BuildEvents.OnBuildDone += (scope, action) =>
            {
                if (buildSucceeded) Console.WriteLine("Build succeeded.");
                else Console.WriteLine("Build failed.");
                cts.Cancel();
            };
            dte.Events.BuildEvents.OnBuildProjConfigDone += (project, config, platform, solutionConfig, success) => { if (!success) buildSucceeded = false; };
            dte.ExecuteCommand("File.SaveAll");
            dte.ExecuteCommand("Build.BuildSolution");
            Console.WriteLine("Build command sent successfully.");
            // Wait for build to complete or timeout after 2 minutes
            try { Task.Delay(120000, cts.Token).Wait(); } catch { }
            return buildSucceeded;
        }
        private static void OpenCodeFileInVS(DTE2? dte, string filePath)
        {
            if (dte == null || string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            Window solutionExplorer = dte.Windows.Item(Constants.vsWindowKindSolutionExplorer);
            UIHierarchy hierarchy = (UIHierarchy)solutionExplorer.Object;
            var projectPath = dte.Solution.Properties.Item("Name").Value + "\\" + BaseToolGenerator.GetProjectNameNoExtension(_projectFileName);
            hierarchy.GetItem(projectPath).Select(vsUISelectionType.vsUISelectionTypeSelect);
            Array activeProjects = (Array)dte.ActiveSolutionProjects;
            if (activeProjects == null || activeProjects.Length <= 0) return;
            var myProject = (EnvDTE.Project?)activeProjects.GetValue(0);
            if (myProject == null) return;
            var myItem = myProject.ProjectItems.AddFromFile(filePath);
            var window = myItem.Open(EnvDTE.Constants.vsViewKindCode);
            window.Visible = true;
            window.Activate();
        }
        private static bool BuildSolution(DTE2 dte)
        {
            try
            {
                return SendSaveAndBuild(dte);
            }
            catch (COMException ex)
            {
                Console.WriteLine("Could not find a running Visual Studio instance: " + ex.Message);
                return false;
            }
        }
        private static ToolResult Opened(DTE2? dte, ToolResult result)
        {
            if (result.FileToOpen != null) OpenCodeFileInVS(dte, result.FileToOpen);
            return result;
        }
        private static ToolResult? DetermineCreation()
        {
            var input = new ToolInput(_values, _headless, new SelectionDialogPrompt());
            if (string.IsNullOrEmpty(_solutionFileName) && string.IsNullOrEmpty(_solutionDir) && string.IsNullOrEmpty(_projectFileName))
                return SolutionGeneratorTool.CreateNewSolution(input);
            // Headless callers build the solution themselves and have no Visual Studio to attach to
            var dte = _headless ? null : GetDTE2();
            if (!_headless && (dte == null || !BuildSolution(dte))) return null;
            bool haveSolution = !string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir);
            if (haveSolution && string.IsNullOrEmpty(_projectFileName)) return ProjectGeneratorTool.CreateNewProjects(_solutionDir, _solutionFileName, input);
            if (!haveSolution || string.IsNullOrEmpty(_projectFileName) || string.IsNullOrEmpty(_targetDirectory)) return null;
            if (!string.IsNullOrEmpty(_permitIfStateFilePath)) return PermitIfGaurdGeneratorTool.CreatePermitIfGuard(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, _permitIfStateFilePath, input);
            if (!string.IsNullOrEmpty(_permitReentryIfStateFilePath)) return PermitReentryIfGaurdGeneratorTool.CreatePermitReentryIfGuard(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, _permitReentryIfStateFilePath, input);
            if (!string.IsNullOrEmpty(_ignoreIfStateFilePath)) return IgnoreIfGaurdGeneratorTool.CreateIgnoreIfGuard(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, _ignoreIfStateFilePath, input);
            if (!string.IsNullOrEmpty(_periodicTriggerStateFilePath)) return PeriodicTriggerGeneratorTool.CreatePeriodicTrigger(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, _periodicTriggerStateFilePath, input);
            if (_addState) return Opened(dte, StateGeneratorTool.CreateNewState(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, input));
            if (_addTrigger) return Opened(dte, TriggerGeneratorTool.CreateNewTrigger(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, input));
            if (_addEvent) return Opened(dte, EventGeneratorTool.CreateNewEvent(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, input));
            if (_addStateMachine) return Opened(dte, StateMachineGeneratorTool.CreateNewStateMachine(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, input));
            if (_liveMode) return Opened(dte, LiveProxyGeneratorTool.CreateNewLiveProxy(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, _liveTimeoutSeconds, _updateProxyFilePath, input));
            return Opened(dte, ProxyGeneratorTool.CreateNewProxy(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, _updateProxyFilePath, input));
        }
    }
}
