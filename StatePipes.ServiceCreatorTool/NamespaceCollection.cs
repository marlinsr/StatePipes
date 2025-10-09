namespace StatePipes.ServiceCreatorTool
{
    internal class NamespaceCollection : List<string>
    {
        private List<string> _typeFullNameAddedList = new List<string>();
        public void AddNamespace(string element)
        {
            
            if (Contains(element)) return;
            Add(element);
        }
        public void AddNameSpaceFromTypeFullName(string typeFullName, ReferencedAssemblies assemblies)
        {
            if (_typeFullNameAddedList.Contains(typeFullName)) return;
            _typeFullNameAddedList.Add(typeFullName);
            var td = assemblies.GetTypeDescription(typeFullName);
            if (td == null) return;
            if (string.IsNullOrEmpty(td.Namespace))
            {
                throw new Exception($"Namespace not found for type {td.FullName}");
            }
            AddNamespace(td.Namespace);
        }
    }
}
