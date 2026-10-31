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
        public static ToolResult CreateNewEvent(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, IToolInput input)
        {
            if (!IsServiceProject(projectFileName)) return ToolResult.Failed;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var eventName = input.Name("--event", "Enter the name for the new event");
            if (eventName == null || AlreadyExists(solutionDir, Path.Combine(projDir, "Events", $"{eventName}.cs"))) return ToolResult.Failed;
            return ToolResult.Ok((new EventGeneratorTool(solutionDir, solutionFileName)).GenerateEvent(projDir, projectName, targetDirectory, eventName));
        }
    }
}
