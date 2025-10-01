namespace StatePipes.ServiceCreatorTool
{
    public class TypeNames
    {
        public TypeNames(string qualifiedName, string fullName, string assemblyName, string @namespace)
        {
            QualifiedName = qualifiedName;
            FullName = fullName;
            AssemblyName = assemblyName;
            Namespace = @namespace;
        }
        public TypeNames() { }
        public string QualifiedName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string AssemblyName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public void SetNames(Type t)
        {
            QualifiedName = t.AssemblyQualifiedName ?? string.Empty;
            FullName = t.FullName ?? string.Empty;
            AssemblyName = t.Assembly.FullName ?? string.Empty;
            Namespace = t.Namespace ?? string.Empty;
        }

    }
}
