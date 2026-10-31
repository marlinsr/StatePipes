namespace StatePipes.ServiceCreatorTool
{
    internal class PermitReentryIfGaurdGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        public void GeneratePermitReentryIfGuard(string projectName, string stateFilePath, string triggerName, bool triggerIsInternal)
        {
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            monikers.AddMoniker("@#$TriggerName@#$", triggerName);

            var stateFileDirectory = Path.GetDirectoryName(stateFilePath)!;
            var stateFileName = Path.GetFileName(stateFilePath);
            var helper = new GeneratorHelper(new DirectoryHelper(stateFileDirectory), monikers);
            helper.Inject("PermitReentryIfGuard_cs.sample", stateFileName, guardInjection1Moniker);
            if (triggerIsInternal) helper.ReplaceInFile(stateFileName, internalTriggerUsingCommentMoniker, string.Empty);
            else helper.ReplaceInFile(stateFileName, publicTriggerUsingCommentMoniker, string.Empty);
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
            return input.Choose("--trigger", triggerNames, "Select the trigger for the PermitReentryIf guard:", fromBuild: true);
        }

        public static ToolResult CreatePermitReentryIfGuard(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, string stateFilePath, IToolInput input)
        {
            if (!IsServiceProject(projectFileName)) return ToolResult.Failed;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var stateMachineName = StateMachineTypesHelper.GetStateMachineNameFromSourceFile(stateFilePath);
            if (string.IsNullOrEmpty(stateMachineName)) return ToolResult.Failed;
            var stateMachineTypesHelper = new StateMachineTypesHelper(projectName, new PathHelper(solutionDir, projDir, projectName, targetDirectory));
            var selectedTrigger = SelectTrigger(stateMachineTypesHelper, stateMachineName, input);
            if (string.IsNullOrEmpty(selectedTrigger)) return ToolResult.Failed;
            (new PermitReentryIfGaurdGeneratorTool(solutionDir, solutionFileName)).GeneratePermitReentryIfGuard(projectName, stateFilePath, selectedTrigger, stateMachineTypesHelper.IsTriggerInternal(selectedTrigger));
            return ToolResult.Ok();
        }
    }
}
