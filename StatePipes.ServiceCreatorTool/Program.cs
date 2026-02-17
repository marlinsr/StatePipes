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
        private static bool DetermineCreation()
        {
            if (string.IsNullOrEmpty(_solutionFileName) && string.IsNullOrEmpty(_solutionDir) && string.IsNullOrEmpty(_projectFileName))
            {
                SolutionGeneratorTool.CreateNewSolution();
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && string.IsNullOrEmpty(_projectFileName))
            {
                ProjectGeneratorTool.CreateNewProjects(_solutionDir, _solutionFileName);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory) && _addState)
            {
                StateGeneratorTool.CreateNewState(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory) && _addTrigger)
            {
                TriggerGeneratorTool.CreateNewTrigger(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory) && _addStateMachine)
            {
                StateMachineGeneratorTool.CreateNewStateMachine(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory);
                return true;
            }
            if (!string.IsNullOrEmpty(_solutionFileName) && !string.IsNullOrEmpty(_solutionDir) && !string.IsNullOrEmpty(_projectFileName) && !string.IsNullOrEmpty(_targetDirectory))
            {
                ProxyGeneratorTool.CreateNewProxy(_solutionDir, _solutionFileName, _projectFileName, _targetDirectory);
                return true;
            }
            return false;
        }
    }
}
