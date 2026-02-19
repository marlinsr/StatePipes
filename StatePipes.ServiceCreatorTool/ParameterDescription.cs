namespace StatePipes.ServiceCreatorTool
{
    public class ParameterDescription(string name, string fullName, bool isNullable, List<AttributeDescription> attributes)
    {
        public List<AttributeDescription> Attributes { get; } = attributes;
        public string FullName { get; } = fullName;
        public string Name { get; } = name;
        public bool IsNullable { get; } = isNullable;
    }
}
