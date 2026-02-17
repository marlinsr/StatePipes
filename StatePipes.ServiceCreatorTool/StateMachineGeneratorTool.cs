namespace StatePipes.ServiceCreatorTool
{
    internal class StateMachineGeneratorTool : BaseToolGenerator
    {
        public StateMachineGeneratorTool(string solutionDir, string solutionFileName) : base(solutionDir, solutionFileName) { }
        public void GenerateStateMachine(string projectDir, string projectName, string targetDirectory, string stateMachineName, bool isStateMachine)
        {
            if (!isStateMachine) throw new ArgumentException("This constructor is only for generating state machines. For other uses, please use the appropriate constructor overload.");
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            monikers.AddMoniker("@#$StateMachineName@#$", stateMachineName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            GenerateStateMachineFiles(helper);
        }
        public static void GenerateStateMachineFiles(GeneratorHelper helper)
        {
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$");
            helper.MoveTo("StateMachines");
            helper.MoveTo("@#$StateMachineName@#$");
            helper.SaveTextFile("StateMachine_cs.sample", "@#$StateMachineName@#$.cs");
            helper.MoveTo("States");
            helper.SaveTextFile("TopLevelState_cs.sample", "TopLevelState.cs");
        }
        public static void CreateNewStateMachine(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory)
        {
            if (!IsServiceProject(projectFileName)) return;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
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
                (new StateMachineGeneratorTool(solutionDir, solutionFileName)).GenerateStateMachine(projectDir, projectName, configuration, answer, isStateMachine: true);
            }
        }
    }
}
