namespace StatePipes.ServiceCreatorTool
{
    internal class TriggerGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        public string GenerateTrigger(string projectDir, string projectName, string targetDirectory, string stateMachineName, string triggerName, bool isInternal)
        {
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            monikers.AddMoniker("@#$StateMachineName@#$", stateMachineName);
            monikers.AddMoniker("@#$TriggerName@#$", triggerName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            return GenerateTriggerFile(isInternal, helper);
        }
        public static string GenerateTriggerFile(bool isInternal, GeneratorHelper helper)
        {
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$");
            helper.MoveTo("StateMachines");
            helper.MoveTo("@#$StateMachineName@#$");
            helper.MoveTo("Triggers");
            if (isInternal)
            {
                helper.MoveTo("Internal");
                return helper.SaveTextFile("InternalTrigger_cs.sample", "@#$TriggerName@#$.cs");
            }
            else
            {
                return helper.SaveTextFile("Trigger_cs.sample", "@#$TriggerName@#$.cs");
            }
        }
        public static string CreateNewTrigger(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory)
        {
            if (!IsServiceProject(projectFileName)) return string.Empty;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var stateMachineTypesHelper = new StateMachineTypesHelper(projectName, new PathHelper(solutionDir, projDir, projectName, targetDirectory));
            var selectedStateMachine = stateMachineTypesHelper.GetStateMachineName();
            if (string.IsNullOrEmpty(selectedStateMachine)) return string.Empty;
            var scopeOptions = new List<string> { "Public", "Internal" };
            var selectedScope = SelectionDialog.ShowListSelection(scopeOptions, "Select the trigger scope");
            if (string.IsNullOrEmpty(selectedScope)) return string.Empty;
            bool isInternal = selectedScope == "Internal";
            var triggerName = "";
            if (!SelectionDialog.GetUserInput(ref triggerName, $"Enter the name for the new trigger on {selectedStateMachine}")) return string.Empty;
            return (new TriggerGeneratorTool(solutionDir, solutionFileName)).GenerateTrigger(projDir, projectName, targetDirectory, selectedStateMachine, triggerName, isInternal);
        }
    }
}
