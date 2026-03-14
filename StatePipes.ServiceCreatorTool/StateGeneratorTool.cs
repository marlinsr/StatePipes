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
        private static string CreateStateClass(string solutionDir, string solutionFileName, string projDir, string projectName, 
            string targetDirectory, string selectedStateMachine, bool isFirstChild = false, string? parentedStateName = null)
        {
            string stateNane = "";
            if (SelectionDialog.ShowInputDialog(ref stateNane, $"Enter the name for the new state on {selectedStateMachine}") == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(stateNane))
                {
                    Console.WriteLine($"Bad state name");
                    return string.Empty;
                }
                return (new StateGeneratorTool(solutionDir, solutionFileName)).GenerateState(projDir, projectName, targetDirectory, selectedStateMachine, stateNane, parentStateName: parentedStateName, isFirstChild: isFirstChild);
            }
            return string.Empty;
        }
        private static string CreateParentedState(string solutionDir, string solutionFileName, string projDir, string projectName, 
            string targetDirectory, string selectedStateMachine, StateMachineTypesHelper stateMachineTypesHelper)
        {
            var stateTypes = stateMachineTypesHelper.GetStateTypes(selectedStateMachine);
            if (stateTypes.Count == 0)
            {
                Console.WriteLine("No existing states found to use as parent.");
                return string.Empty;
            }
            var stateNames = stateTypes.Select(t => t.Name).ToList();
            var selectedParent = SelectionDialog.ShowListSelection(stateNames, "Select the parent state");
            if (selectedParent == null) return string.Empty;
            var isFirstChild = !stateTypes.Any(t =>
            {
                var genArgs = t.BaseType?.GetGenericArguments();
                return genArgs != null && genArgs.Length >= 2 && genArgs[1].Name == selectedParent;
            });
            return CreateStateClass(solutionDir, solutionFileName, projDir, projectName, targetDirectory, selectedStateMachine, 
                isFirstChild: isFirstChild, parentedStateName: selectedParent);
        }
        public static string CreateNewState(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory)
        {
            if (!IsServiceProject(projectFileName)) return string.Empty;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var stateMachineTypesHelper = new StateMachineTypesHelper(projectName, new PathHelper(solutionDir, projDir, projectName, targetDirectory));
            var selectedStateMachine = stateMachineTypesHelper.GetStateMachineName();
            if (selectedStateMachine == null) return string.Empty;
            var stateTypeOptions = new List<string> { "Un-parented", "Parented" };
            var selectedStateType = SelectionDialog.ShowListSelection(stateTypeOptions, "Select the state type");
            if (string.IsNullOrEmpty(selectedStateType)) return string.Empty;
            if (selectedStateType == "Un-parented") return CreateStateClass(solutionDir, solutionFileName, projDir, projectName, targetDirectory, selectedStateMachine);
            else return CreateParentedState(solutionDir, solutionFileName, projDir, projectName, targetDirectory, selectedStateMachine, stateMachineTypesHelper);
        }
    }
}
