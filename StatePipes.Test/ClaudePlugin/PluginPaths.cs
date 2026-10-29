namespace StatePipes.Test.ClaudePlugin
{
    internal static class PluginPaths
    {
        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StatePipes.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException($"No StatePipes.slnx above {AppContext.BaseDirectory}");
        }
        public static string Skills => Path.Combine(RepoRoot(), "StatePipes.ClaudePlugin", "skills");
        public static string Operations => Path.Combine(RepoRoot(), "StatePipes.ClaudePlugin", "skills", "statepipes-scaffold", "operations");
        public static string Resources => Path.Combine(RepoRoot(), "StatePipes.ServiceCreatorTool", "Resources");
        public static string BaseToolGenerator => Path.Combine(RepoRoot(), "StatePipes.ServiceCreatorTool", "BaseToolGenerator.cs");
    }
}
