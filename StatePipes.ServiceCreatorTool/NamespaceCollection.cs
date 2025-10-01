namespace StatePipes.ServiceCreatorTool
{
    internal class NamespaceCollection : List<string>
    {
        private List<string> _typeAssemblyQualifiedNameAddedList = new List<string>();
        public void AddNamespace(string element)
        {
            
            if (Contains(element)) return;
            Add(element);
        }
        public void AddNameSpaceFromTypeAssemblyQualifiedName(string typeAssemblyQualifiedName, ReferencedAssemblies assemblies)
        {
            if (_typeAssemblyQualifiedNameAddedList.Contains(typeAssemblyQualifiedName)) return;
            _typeAssemblyQualifiedNameAddedList.Add(typeAssemblyQualifiedName);
            var td = assemblies.GetTypeDescription(typeAssemblyQualifiedName);
            if (td == null) return;
            if (string.IsNullOrEmpty(td.Namespace))
            {
                throw new Exception($"Namespace not found for type {td.FullName}");
            }
            AddNamespace(td.Namespace);
        }
    }
}
