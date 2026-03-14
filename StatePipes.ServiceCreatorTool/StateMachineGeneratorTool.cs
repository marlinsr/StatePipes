namespace StatePipes.ServiceCreatorTool
{
    internal class StateMachineGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        public string GenerateStateMachine(string projectDir, string projectName, string targetDirectory, string stateMachineName, bool isStateMachine)
        {
            if (!isStateMachine) throw new ArgumentException("This constructor is only for generating state machines. For other uses, please use the appropriate constructor overload.");
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            monikers.AddMoniker("@#$StateMachineName@#$", stateMachineName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            return GenerateStateMachineFiles(helper);
        }
        public static string GenerateStateMachineFiles(GeneratorHelper helper)
        {
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$");
            helper.MoveTo("StateMachines");
            helper.MoveTo("@#$StateMachineName@#$");
            helper.SaveTextFile("StateMachine_cs.sample", "@#$StateMachineName@#$.cs");
            helper.MoveTo("States");
            return helper.SaveTextFile("TopLevelState_cs.sample", "TopLevelState.cs");
        }
        public static string CreateNewStateMachine(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory)
        {
            if (!IsServiceProject(projectFileName)) return string.Empty;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            return AddNewStateMachine(solutionDir, solutionFileName, projDir, projectName, targetDirectory);
        }
        private static string AddNewStateMachine(string solutionDir, string solutionFileName, string projectDir, string projectName, string configuration)
        {
            string answer = "";
            if (SelectionDialog.ShowInputDialog(ref answer, "Enter the name for the new state machine") == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(answer))
                {
                    Console.WriteLine($"Bad state machine name: {answer}");
                    return string.Empty;
                }
                return (new StateMachineGeneratorTool(solutionDir, solutionFileName)).GenerateStateMachine(projectDir, projectName, configuration, answer, isStateMachine: true);
            }
            return string.Empty;
        }
    }
}
