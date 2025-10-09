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
        public void RegisterNeedsCreating(string typeFullName)
        {
            if (!_valueObjectTypesCreatedList.Contains(typeFullName) && !_valueObjectTypeToCreateList.Contains(typeFullName))
                _valueObjectTypeToCreateList.Add(typeFullName);
        }
        public int CountNeedsCreating => _valueObjectTypeToCreateList.Count;
        public void RegisterCreatedValueObject(string typeFullName)
        {
            if (_valueObjectTypeToCreateList.Contains(typeFullName)) _valueObjectTypeToCreateList.Remove(typeFullName);
            if (!_valueObjectTypesCreatedList.Contains(typeFullName)) _valueObjectTypesCreatedList.Add(typeFullName);
        }
    }
}
