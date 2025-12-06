using System.Reflection;
using System.Runtime.Loader;
using Autofac.Util;
namespace StatePipes.Diagrammer
{
    internal class AssemblyManager
    {
        private Dictionary<string, Assembly> _loadedAssemblies = new Dictionary<string, Assembly>();
        public AssemblyManager(string path)
        {
            var files = Directory.GetFiles(path).Where(f => f.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase)).ToList();
            files.ForEach(file =>
            {
                try
                {
                    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
                    var assmName = assembly.GetName().Name;
                    if (assmName != null && assembly != null) _loadedAssemblies.Add(assmName, assembly);
                }
                catch { }
            });
        }
        public Type? GetTypeFromAssemblies(string fullName)
        {
            foreach (var assembly in _loadedAssemblies.Values)
            {
                var t = assembly.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
        public List<Assembly> GetAssembliesContainingType(Type t)
        {
            List<Assembly> assemblies = new List<Assembly>();
            foreach (var assembly in _loadedAssemblies.Values)
            {
                if (!t.Assembly.Equals(assembly))
                {
                    var assemblyTypes = assembly.GetLoadableTypes();
                    var b = assemblyTypes.Where(s => t.IsAssignableFrom(s) && !t.Equals(s));
                    if (b.Any()) assemblies.Add(assembly);
                }
            }
            return assemblies;
        }
    }
}
