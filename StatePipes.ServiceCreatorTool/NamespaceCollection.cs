namespace StatePipes.ServiceCreatorTool
{
    internal class NamespaceCollection : List<string>
    {
        private readonly List<string> _typeFullNameAddedList = [];
        public void AddNamespace(string element)
        {

            if (Contains(element)) return;
            if (element.StartsWith("System") || element.StartsWith("StatePipes")) Add(element);
        }
        public void AddNameSpaceFromTypeFullName(string typeFullName, IServiceTypeSource source)
        {
            if (_typeFullNameAddedList.Contains(typeFullName)) return;
            _typeFullNameAddedList.Add(typeFullName);
            var td = source.GetTypeDescription(typeFullName);
            if (td == null) return;
            if (string.IsNullOrEmpty(td.Namespace))
            {
                throw new Exception($"Namespace not found for type {td.FullName}");
            }
            // Any other type is generated into the proxy's own ValueObjects namespace, so its source namespace is never referenced.
            if (td.Namespace.StartsWith("System") || td.Namespace.StartsWith("StatePipes.Common")) AddNamespace(td.Namespace);
        }
    }
}
