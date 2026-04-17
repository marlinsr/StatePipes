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
            TriggerGeneratorTool.GenerateTriggerFile(true, triggerHelper);
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
        public static void CreatePeriodicTrigger(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, string stateFilePath)
        {
            if (!IsServiceProject(projectFileName)) return;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var stateMachineName = StateMachineTypesHelper.GetStateMachineNameFromSourceFile(stateFilePath);
            if (string.IsNullOrEmpty(stateMachineName)) return;
            string timerName = "";
            if (!SelectionDialog.GetUserInput(ref timerName, $"Enter the timer name for {stateMachineName}")) return;
            string timerPeriod = "";
            if (!SelectionDialog.GetUserInput(ref timerPeriod, $"Enter the period in milliseconds for {timerName}")) return;
            if (string.IsNullOrEmpty(timerPeriod)) return;
            (new PeriodicTriggerGeneratorTool(solutionDir, solutionFileName)).GeneratePeriodicTrigger(projDir, projectName, targetDirectory, stateFilePath, stateMachineName, timerName, timerPeriod);
        }
    }
}
