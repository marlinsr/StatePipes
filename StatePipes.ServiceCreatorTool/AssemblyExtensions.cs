using System.Reflection;

namespace StatePipes.ServiceCreatorTool
{
    public static class AssemblyExtensions
    {
        public static IEnumerable<Type> GetTypesNoExceptions(this Assembly assembly)
        {
            if (assembly is null) throw new Exception("assembly is null");
            try
            {
                return assembly.DefinedTypes.Select(t => t.AsType());
            }
            catch (ReflectionTypeLoadException e)
            {
                return (IEnumerable<Type>)e.Types.Where(t => t != null);
            }
        }
    }
}
