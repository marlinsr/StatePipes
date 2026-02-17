namespace StatePipes.ServiceCreatorTool
{
    internal class TriggerGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        public void GenerateTrigger(string projectDir, string projectName, string targetDirectory, string stateMachineName, string triggerName, bool isInternal)
        {
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            monikers.AddMoniker("@#$StateMachineName@#$", stateMachineName);
            monikers.AddMoniker("@#$TriggerName@#$", triggerName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            GenerateTriggerFile(isInternal, helper);
        }
        public static void GenerateTriggerFile(bool isInternal, GeneratorHelper helper)
        {
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$");
            helper.MoveTo("StateMachines");
            helper.MoveTo("@#$StateMachineName@#$");
            helper.MoveTo("Triggers");
            if (isInternal)
            {
                helper.MoveTo("Internal");
                helper.SaveTextFile("InternalTrigger_cs.sample", "@#$TriggerName@#$.cs");
            }
            else
            {
                helper.SaveTextFile("Trigger_cs.sample", "@#$TriggerName@#$.cs");
            }
        }
        public static void CreateNewTrigger(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory)
        {
            if (!IsServiceProject(projectFileName)) return;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var stateMachineTypesHelper = new StateMachineTypesHelper(projectName, new PathHelper(solutionDir, projDir, projectName, targetDirectory));
            var selectedStateMachine = stateMachineTypesHelper.GetStateMachineName();
            if (string.IsNullOrEmpty(selectedStateMachine)) return;
            var scopeOptions = new List<string> { "Public", "Internal" };
            var selectedScope = SelectionDialog.ShowListSelection(scopeOptions, "Select the trigger scope");
            if (string.IsNullOrEmpty(selectedScope)) return;
            bool isInternal = selectedScope == "Internal";
            var answer = GetTriggerName(selectedStateMachine);
            if (string.IsNullOrEmpty(answer)) return;
            (new TriggerGeneratorTool(solutionDir, solutionFileName)).GenerateTrigger(projDir, projectName, targetDirectory, selectedStateMachine, answer, isInternal);
        }
        private static string? GetTriggerName(string selectedStateMachine)
        {
            string triggerName = "";
            if (SelectionDialog.ShowInputDialog(ref triggerName, $"Enter the name for the new trigger on {selectedStateMachine}") == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(triggerName))
                {
                    Console.WriteLine($"Bad trigger name");
                    return null;
                }
                return triggerName;
            }
            Console.WriteLine($"Action canceled");
            return null;
        }
    }
}
