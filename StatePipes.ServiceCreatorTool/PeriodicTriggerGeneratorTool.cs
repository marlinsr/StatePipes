namespace StatePipes.ServiceCreatorTool
{
    internal class PeriodicTriggerGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        public void GeneratePeriodicTrigger(string projectDir, string projectName, string targetDirectory, string stateFilePath, string stateMachineName, string timerName, string timerPeriod)
        {
            var normalizedTimerName = NormalizeName(timerName);
            var triggerName = normalizedTimerName + "Trigger";
            var periodicGuard = normalizedTimerName + "PeriodicWorker";
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            monikers.AddMoniker("@#$StateMachineName@#$", stateMachineName);
            monikers.AddMoniker("@#$TriggerName@#$", triggerName);
            monikers.AddMoniker("@#$TimerName@#$", timerName);
            monikers.AddMoniker("@#$TimerPeriod@#$", timerPeriod);
            monikers.AddMoniker("@#$PeriodicGuard@#$", periodicGuard);
            var triggerHelper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            // The same timer in another state of the machine reuses its trigger rather than overwriting it
            var triggerFile = Path.Combine(projectDir, "StateMachines", stateMachineName, "Triggers", "Internal", $"{triggerName}.cs");
            if (!File.Exists(triggerFile)) TriggerGeneratorTool.GenerateTriggerFile(true, triggerHelper);
            StateFileInjection(stateFilePath, monikers);
        }
        private static void StateFileInjection(string stateFilePath, MonikerSubstitution monikers)
        {
            var stateFileDirectory = Path.GetDirectoryName(stateFilePath)!;
            var stateFileName = Path.GetFileName(stateFilePath);
            var stateHelper = new GeneratorHelper(new DirectoryHelper(stateFileDirectory), monikers);
            stateHelper.Inject("PeriodicField_cs.sample", stateFileName, periodicFieldInjection1Moniker);
            stateHelper.Inject("PeriodicConstructor_cs.sample", stateFileName, periodicConstructorInjection1Moniker);
            stateHelper.Inject("PeriodicOnEntry_cs.sample", stateFileName, periodicOnEntryInjection1Moniker);
            stateHelper.Inject("PeriodicOnExit_cs.sample", stateFileName, periodicOnExitInjection1Moniker);
            stateHelper.Inject("PeriodicGuard_cs.sample", stateFileName, guardInjection1Moniker);
            stateHelper.ReplaceInFile(stateFileName, internalTriggerUsingCommentMoniker, string.Empty);
        }
        private static string NormalizeName(string input)
        {
            int i = 0;
            while (i < input.Length && !char.IsLetter(input[i])) i++;
            if (i >= input.Length) return string.Empty;
            return char.ToUpper(input[i]) + input[(i + 1)..];
        }
        public static ToolResult CreatePeriodicTrigger(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, string stateFilePath, IToolInput input)
        {
            if (!IsServiceProject(projectFileName)) return ToolResult.Failed;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var stateMachineName = StateMachineTypesHelper.GetStateMachineNameFromSourceFile(stateFilePath);
            if (string.IsNullOrEmpty(stateMachineName)) return ToolResult.Failed;
            var timerName = input.Name("--timer-name", $"Enter the timer name for {stateMachineName}");
            if (timerName == null) return ToolResult.Failed;
            var timerPeriod = input.PositiveInt("--timer-period", $"Enter the period in milliseconds for {timerName}");
            if (timerPeriod == null) return ToolResult.Failed;
            (new PeriodicTriggerGeneratorTool(solutionDir, solutionFileName)).GeneratePeriodicTrigger(projDir, projectName, targetDirectory, stateFilePath, stateMachineName, timerName, timerPeriod.Value.ToString());
            return ToolResult.Ok();
        }
    }
}
