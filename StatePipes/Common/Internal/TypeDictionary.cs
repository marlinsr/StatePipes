namespace StatePipes.Common.Internal
{
    //For dynamically created types used by the explorer
    internal class TypeDictionary
    {
        private Dictionary<string, Type> _typeDictionary = [];
        public void Add(Type type)
        {
            lock (_typeDictionary)
            {
                var assemblyQualifiedTypeName = type.AssemblyQualifiedName;
                if (string.IsNullOrEmpty(assemblyQualifiedTypeName)) return;
                if (_typeDictionary.ContainsKey(assemblyQualifiedTypeName)) return;
                _typeDictionary[assemblyQualifiedTypeName] = type;
            }
        }
        public Type? Get(string assemblyQualifiedTypeName)
        {
            lock (_typeDictionary)
            {
                if (!_typeDictionary.TryGetValue(assemblyQualifiedTypeName, out Type? value)) return null;
                return value;
            }
        }
    }
}
