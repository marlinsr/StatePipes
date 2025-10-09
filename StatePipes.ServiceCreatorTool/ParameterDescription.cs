namespace StatePipes.ServiceCreatorTool
{
    public class ParameterDescription
    {
        public ParameterDescription(string name, string fullName, bool isNullable, List<AttributeDescription> attributes)
        {
            Attributes = attributes;
            FullName = fullName;
            Name = name;
            IsNullable = isNullable;
        }
        public List<AttributeDescription> Attributes { get; }
        public string FullName { get; }
        public string Name { get; }
        public bool IsNullable { get; }
    }
}
