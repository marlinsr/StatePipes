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
        public static ToolResult CreateNewTrigger(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, IToolInput input)
        {
            if (!IsServiceProject(projectFileName)) return ToolResult.Failed;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var stateMachineTypesHelper = new StateMachineTypesHelper(projectName, new PathHelper(solutionDir, projDir, projectName, targetDirectory));
            var selectedStateMachine = stateMachineTypesHelper.GetStateMachineName(input);
            if (string.IsNullOrEmpty(selectedStateMachine)) return ToolResult.Failed;
            var selectedScope = input.Choose("--trigger-scope", ["Public", "Internal"], "Select the trigger scope");
            if (selectedScope == null) return ToolResult.Failed;
            bool isInternal = selectedScope == "Internal";
            var triggerName = input.Name("--trigger-name", $"Enter the name for the new trigger on {selectedStateMachine}");
            if (triggerName == null) return ToolResult.Failed;
            var triggerDir = Path.Combine(projDir, "StateMachines", selectedStateMachine, "Triggers");
            if (AlreadyExists(solutionDir, Path.Combine(isInternal ? Path.Combine(triggerDir, "Internal") : triggerDir, $"{triggerName}.cs"))) return ToolResult.Failed;
            return ToolResult.Ok((new TriggerGeneratorTool(solutionDir, solutionFileName)).GenerateTrigger(projDir, projectName, targetDirectory, selectedStateMachine, triggerName, isInternal));
        }
    }
}
