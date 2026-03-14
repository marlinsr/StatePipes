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
        static bool _addState = false;
        static string _permitIfStateFilePath = string.Empty;
        static string _permitReentryIfStateFilePath = string.Empty;
        static string _ignoreIfStateFilePath = string.Empty;
        static string _periodicTriggerStateFilePath = string.Empty;
        static string _updateProxyFilePath = string.Empty;
        private static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("-r", StringComparison.CurrentCultureIgnoreCase)) _solutionDir = args[++i];
                if (args[i].Equals("-s", StringComparison.CurrentCultureIgnoreCase)) _solutionFileName = args[++i];
                if (args[i].Equals("-p", StringComparison.CurrentCultureIgnoreCase)) _projectFileName = args[++i];
                if (args[i].Equals("-b", StringComparison.CurrentCultureIgnoreCase)) _targetDirectory = args[++i];
                if (args[i].Equals("-m", StringComparison.CurrentCultureIgnoreCase)) _addStateMachine = true;
                if (args[i].Equals("-t", StringComparison.CurrentCultureIgnoreCase)) _addTrigger = true;
                if (args[i].Equals("-a", StringComparison.CurrentCultureIgnoreCase)) _addState = true;
                if (args[i].Equals("-pi", StringComparison.CurrentCultureIgnoreCase)) _permitIfStateFilePath = args[++i];
                if (args[i].Equals("-pri", StringComparison.CurrentCultureIgnoreCase)) _permitReentryIfStateFilePath = args[++i];
                if (args[i].Equals("-ii", StringComparison.CurrentCultureIgnoreCase)) _ignoreIfStateFilePath = args[++i];
                if (args[i].Equals("-pti", StringComparison.CurrentCultureIgnoreCase)) _periodicTriggerStateFilePath = args[++i];
                if (args[i].Equals("-u", StringComparison.CurrentCultureIgnoreCase)) _updateProxyFilePath = args[++i];
            }
        }
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
        private static void OpenCodeFileInVS(DTE2 dte, string filePath)
        {
            if(string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            Array activeProjects = (Array)dte.ActiveSolutionProjects;
            if (activeProjects != null && activeProjects.Length > 0)
            {
                var myProject = (EnvDTE.Project?)activeProjects.GetValue(0);
                if (myProject == null) return;
                var myItem = myProject.ProjectItems.AddFromFile(filePath);
                var window = myItem.Open(EnvDTE.Constants.vsViewKindCode);
                window.Visible = true;
                window.Activate();
            }
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
        private static bool DetermineCreation()
        {
            if (string.IsNullOrEmpty(_solutionFileName) && string.IsNullOrEmpty(_solutionDir) && string.IsNullOrEmpty(_projectFileName))
            {
                SolutionGeneratorTool.CreateNewSolution();
                return true;
            }
            var dte = GetDTE2();
            if (dte == null) return false;
            if (!BuildSolution(dte)) return false;
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && string.IsNullOrEmpty(_projectFileName))
            {
                ProjectGeneratorTool.CreateNewProjects(_solutionDir, _solutionFileName);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory) && !string.IsNullOrEmpty(_permitIfStateFilePath))
            {
                PermitIfGaurdGeneratorTool.CreatePermitIfGuard(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, _permitIfStateFilePath);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory) && !string.IsNullOrEmpty(_permitReentryIfStateFilePath))
            {
                PermitReentryIfGaurdGeneratorTool.CreatePermitReentryIfGuard(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, _permitReentryIfStateFilePath);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory) && !string.IsNullOrEmpty(_ignoreIfStateFilePath))
            {
                IgnoreIfGaurdGeneratorTool.CreateIgnoreIfGuard(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, _ignoreIfStateFilePath);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory) && !string.IsNullOrEmpty(_periodicTriggerStateFilePath))
            {
                PeriodicTriggerGeneratorTool.CreatePeriodicTrigger(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, _periodicTriggerStateFilePath);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory) && _addState)
            {
                var fileName = StateGeneratorTool.CreateNewState(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory);
                OpenCodeFileInVS(dte, fileName);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory) && _addTrigger)
            {
                var fileName = TriggerGeneratorTool.CreateNewTrigger(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory);
                OpenCodeFileInVS(dte, fileName);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory) && _addStateMachine)
            {
                var fileName = StateMachineGeneratorTool.CreateNewStateMachine(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory);
                OpenCodeFileInVS(dte, fileName);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory))
            {
                var fileName = ProxyGeneratorTool.CreateNewProxy(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory, _updateProxyFilePath);
                OpenCodeFileInVS(dte, fileName);
                return true;
            }
            return false;
        }
    }
}
