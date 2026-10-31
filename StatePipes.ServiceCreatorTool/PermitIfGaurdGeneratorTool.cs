namespace StatePipes.ServiceCreatorTool
{
    internal class PermitIfGaurdGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        public void GeneratePermitIfGuard(string projectName, string stateFilePath, string triggerName, bool triggerIsInternal, string transitionToStateName)
        {
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            monikers.AddMoniker("@#$TriggerName@#$", triggerName);
            monikers.AddMoniker("@#$TransitionToStateName@#$", transitionToStateName);
            var stateFileDirectory = Path.GetDirectoryName(stateFilePath)!;
            var stateFileName = Path.GetFileName(stateFilePath);
            var helper = new GeneratorHelper(new DirectoryHelper(stateFileDirectory), monikers);
            helper.Inject("PermitIfGuard_cs.sample", stateFileName, guardInjection1Moniker);
            if (triggerIsInternal) helper.ReplaceInFile(stateFileName, internalTriggerUsingCommentMoniker, string.Empty);
            else helper.ReplaceInFile(stateFileName, publicTriggerUsingCommentMoniker, string.Empty);
        }

        private static string? SelectState(StateMachineTypesHelper stateMachineTypesHelper, string stateMachineName, IToolInput input)
        {
            var stateTypes = stateMachineTypesHelper.GetStateTypes(stateMachineName);
            if (stateTypes.Count == 0)
            {
                Console.WriteLine("No states found for the state machine.");
                return null;
            }
            var stateNames = stateTypes.Select(t => t.Name).ToList();
            return input.Choose("--transition-to", stateNames, "Select the transition-to state:", fromBuild: true);
        }
        private static string? SelectTrigger(StateMachineTypesHelper stateMachineTypesHelper, string stateMachineName, IToolInput input)
        {
            var triggerTypes = stateMachineTypesHelper.GetTriggerTypes(stateMachineName);
            if (triggerTypes.Count == 0)
            {
                Console.WriteLine("No triggers found for the state machine.");
                return null;
            }
            var triggerNames = triggerTypes.Select(t => t.Name).ToList();
            return input.Choose("--trigger", triggerNames, "Select the trigger for the PermitIf guard:", fromBuild: true);
        }
        public static ToolResult CreatePermitIfGuard(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, string stateFilePath, IToolInput input)
        {
            if (!IsServiceProject(projectFileName)) return ToolResult.Failed;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var stateMachineName = StateMachineTypesHelper.GetStateMachineNameFromSourceFile(stateFilePath);
            if (string.IsNullOrEmpty(stateMachineName)) return ToolResult.Failed;
            var stateMachineTypesHelper = new StateMachineTypesHelper(projectName, new PathHelper(solutionDir, projDir, projectName, targetDirectory));
            var selectedTrigger = SelectTrigger(stateMachineTypesHelper, stateMachineName, input);
            if (string.IsNullOrEmpty(selectedTrigger)) return ToolResult.Failed;
            var selectedState = SelectState(stateMachineTypesHelper, stateMachineName, input);
            if (string.IsNullOrEmpty(selectedState)) return ToolResult.Failed;
            (new PermitIfGaurdGeneratorTool(solutionDir, solutionFileName)).GeneratePermitIfGuard(projectName, stateFilePath, selectedTrigger, stateMachineTypesHelper.IsTriggerInternal(selectedTrigger), selectedState);
            return ToolResult.Ok();
        }
    }
}
