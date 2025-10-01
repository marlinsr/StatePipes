namespace StatePipes.ServiceCreatorTool
{
    internal class ValueObjectsCreationTracker
    {
        private List<string> _valueObjectTypeToCreateList = new();
        private List<string> _valueObjectTypesCreatedList = new();
        public string? GetTopNeedsCreating()
        {
            if (CountNeedsCreating <= 0) return null;
            return _valueObjectTypeToCreateList[0];
        }
        public void RegisterNeedsCreating(string fullyQualifiedTypeName)
        {
            if (!_valueObjectTypesCreatedList.Contains(fullyQualifiedTypeName) && !_valueObjectTypeToCreateList.Contains(fullyQualifiedTypeName))
                _valueObjectTypeToCreateList.Add(fullyQualifiedTypeName);
        }
        public int CountNeedsCreating => _valueObjectTypeToCreateList.Count;
        public void RegisterCreatedValueObject(string fullyQualifiedTypeName)
        {
            if (_valueObjectTypeToCreateList.Contains(fullyQualifiedTypeName)) _valueObjectTypeToCreateList.Remove(fullyQualifiedTypeName);
            if (!_valueObjectTypesCreatedList.Contains(fullyQualifiedTypeName)) _valueObjectTypesCreatedList.Add(fullyQualifiedTypeName);
        }
    }
}
