namespace StatePipes.ServiceCreatorTool
{
    public class ParameterDescription
    {
        public ParameterDescription(string name, string qualifiedName, bool isNullable, List<AttributeDescription> attributes)
        {
            Attributes = attributes;
            QualifiedName = qualifiedName;
            Name = name;
            IsNullable = isNullable;
        }
        public List<AttributeDescription> Attributes { get; }
        public string QualifiedName { get; }
        public string Name { get; }
        public bool IsNullable { get; }
    }
}
