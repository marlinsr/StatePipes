namespace StatePipes.ServiceCreatorTool
{
    public enum PathName
    {
        Root,
        Solution,
        Project,
        Proxies,
        Bin
    }
    public class PathHelper
    {
        private readonly Dictionary<PathName, string> _paths;
        public PathHelper() => _paths = [];
        public PathHelper(string solutionDir) : this()
        {
            var root = Directory.GetParent(solutionDir)!.FullName;
            _paths.Add(PathName.Root, root);
            _paths.Add(PathName.Solution, solutionDir);

        }
        public PathHelper(string solutionDir, string projectDir, string projectName, string targetDirectory) : this(solutionDir)
        {
            AddPaths(projectDir, projectName, targetDirectory);
        }

        public void AddPaths(string projectDir, string projectName, string targetDirectory)
        {
            _paths.Add(PathName.Project, projectDir);
            var configuration = RemovePrefixDirectory(RemovePrefixDirectory(targetDirectory, projectDir), "obj");
            var binDir = Path.Combine(_paths[PathName.Solution], $"{projectName}.Service", "bin", configuration);
            _paths.Add(PathName.Bin, binDir);
            var proxiesDir = Path.Combine(_paths[PathName.Project], "Proxies");
            _paths.Add(PathName.Proxies, proxiesDir);
        }
        public string GetPath(PathName pathName) => _paths[pathName];
        private static string RemovePrefixDirectory(string fullPath, string prefixPath)
        {
            var normalizedFullPath = Path.GetFullPath(fullPath);
            var normalizedPrefixPath = Path.GetFullPath(prefixPath);
            if (normalizedFullPath.StartsWith(normalizedPrefixPath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = normalizedFullPath[normalizedPrefixPath.Length..];
                //Trim any leading directory separators from the result
                return relativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return fullPath;
        }
    }
}
