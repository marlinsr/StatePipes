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
            string stateMachineName = "";
            if (!SelectionDialog.GetUserInput(ref stateMachineName, "Enter the name for the new state machine")) return string.Empty;
            return (new StateMachineGeneratorTool(solutionDir, solutionFileName)).GenerateStateMachine(projDir, projectName, targetDirectory, stateMachineName, isStateMachine: true);
        }
    }
}
