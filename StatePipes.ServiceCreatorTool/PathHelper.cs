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
        private Dictionary<PathName, string> _paths;
        public PathHelper() => _paths = new Dictionary<PathName, string>();
        public PathHelper(string solutionDir) : this()
        {
            var root = Directory.GetParent(solutionDir)!.FullName;
            _paths.Add(PathName.Root, root);
            _paths.Add(PathName.Solution, solutionDir);

        }
        public PathHelper(string solutionDir, string projectDir, string projectName) : this(solutionDir)
        {
            _paths.Add(PathName.Project, projectDir);
            string binDir = Path.Combine(_paths[PathName.Solution], $"{projectName}.Service", "bin", "Debug", "net10.0");
            _paths.Add(PathName.Bin, binDir);
            string proxiesDir = Path.Combine(_paths[PathName.Project], "Proxies");
            _paths.Add(PathName.Proxies, proxiesDir);
        }
        public string GetPath(PathName pathName) => _paths[pathName];
    }
}
