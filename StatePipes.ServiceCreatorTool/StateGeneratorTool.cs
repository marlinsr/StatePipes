namespace StatePipes.ServiceCreatorTool
{
    internal class StateGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        public string GenerateState(string projectDir, string projectName, string targetDirectory, string stateMachineName,
            string stateName, string? parentStateName, bool isFirstChild)
        {
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            monikers.AddMoniker("@#$StateMachineName@#$", stateMachineName);
            monikers.AddMoniker("@#$StateName@#$", stateName);
            if (parentStateName != null) monikers.AddMoniker("@#$ParentStateName@#$", parentStateName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            return GenerateStateFile(parentStateName, isFirstChild, helper);
        }
        public static string GenerateStateFile(string? parentStateName, bool isFirstChild, GeneratorHelper helper)
        {
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$");
            helper.MoveTo("StateMachines");
            helper.MoveTo("@#$StateMachineName@#$");
            helper.MoveTo("States");
            if (parentStateName == null) return helper.SaveTextFile("UnparentedState_cs.sample", "@#$StateName@#$.cs");
            else if (isFirstChild) return helper.SaveTextFile("FirstParentedState_cs.sample", "@#$StateName@#$.cs");
            else return helper.SaveTextFile("ParentedState_cs.sample", "@#$StateName@#$.cs");
        }
        private static ToolResult CreateStateClass(string solutionDir, string solutionFileName, string projDir, string projectName,
            string targetDirectory, string selectedStateMachine, IToolInput input, bool isFirstChild = false, string? parentedStateName = null)
        {
            var stateName = input.Name("--state", $"Enter the name for the new state on {selectedStateMachine}");
            if (stateName == null || AlreadyExists(solutionDir, Path.Combine(projDir, "StateMachines", selectedStateMachine, "States", $"{stateName}.cs"))) return ToolResult.Failed;
            return ToolResult.Ok((new StateGeneratorTool(solutionDir, solutionFileName)).GenerateState(projDir, projectName, targetDirectory, selectedStateMachine, stateName, parentStateName: parentedStateName, isFirstChild: isFirstChild));
        }
        private static ToolResult CreateParentedState(string solutionDir, string solutionFileName, string projDir, string projectName,
            string targetDirectory, string selectedStateMachine, StateMachineTypesHelper stateMachineTypesHelper, IToolInput input)
        {
            var stateTypes = stateMachineTypesHelper.GetStateTypes(selectedStateMachine);
            if (stateTypes.Count == 0)
            {
                Console.WriteLine("No existing states found to use as parent.");
                return ToolResult.Failed;
            }
            var stateNames = stateTypes.Select(t => t.Name).ToList();
            var selectedParent = input.Choose("--parent-state", stateNames, "Select the parent state", fromBuild: true);
            if (selectedParent == null) return ToolResult.Failed;
            var isFirstChild = !stateTypes.Any(t =>
            {
                var genArgs = t.BaseType?.GetGenericArguments();
                return genArgs != null && genArgs.Length >= 2 && genArgs[1].Name == selectedParent;
            });
            return CreateStateClass(solutionDir, solutionFileName, projDir, projectName, targetDirectory, selectedStateMachine, input,
                isFirstChild: isFirstChild, parentedStateName: selectedParent);
        }
        /// <summary>
        /// A supplied --parent-state means parented, and headless without one means un-parented. Otherwise the state type
        /// dialog asks; --state-type is not an accepted argument, so it never answers that dialog.
        /// </summary>
        private static bool? IsParented(IToolInput input)
        {
            if (input.Supplied("--parent-state")) return true;
            if (input.Headless) return false;
            var selectedStateType = input.Choose("--state-type", ["Un-parented", "Parented"], "Select the state type");
            return selectedStateType == null ? null : selectedStateType == "Parented";
        }
        public static ToolResult CreateNewState(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, IToolInput input)
        {
            if (!IsServiceProject(projectFileName)) return ToolResult.Failed;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var stateMachineTypesHelper = new StateMachineTypesHelper(projectName, new PathHelper(solutionDir, projDir, projectName, targetDirectory));
            var selectedStateMachine = stateMachineTypesHelper.GetStateMachineName(input);
            if (selectedStateMachine == null) return ToolResult.Failed;
            var parented = IsParented(input);
            if (parented == null) return ToolResult.Failed;
            if (parented == false) return CreateStateClass(solutionDir, solutionFileName, projDir, projectName, targetDirectory, selectedStateMachine, input);
            return CreateParentedState(solutionDir, solutionFileName, projDir, projectName, targetDirectory, selectedStateMachine, stateMachineTypesHelper, input);
        }
    }
}
