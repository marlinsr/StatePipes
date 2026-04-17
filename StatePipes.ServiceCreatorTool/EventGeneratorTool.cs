namespace StatePipes.ServiceCreatorTool
{
    internal class EventGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        public string GenerateEvent(string projectDir, string projectName, string targetDirectory, string eventName)
        {
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            monikers.AddMoniker("@#$EventName@#$", eventName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$");
            helper.MoveTo("Events");
            return helper.SaveTextFile("Event_cs.sample", "@#$EventName@#$.cs");
        }
        public static string CreateNewEvent(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory)
        {
            if (!IsServiceProject(projectFileName)) return string.Empty;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var eventName = "";
            if (!SelectionDialog.GetUserInput(ref eventName, "Enter the name for the new event")) return string.Empty;
            return (new EventGeneratorTool(solutionDir, solutionFileName)).GenerateEvent(projDir, projectName, targetDirectory, eventName);
        }
    }
}
