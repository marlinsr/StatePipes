namespace StatePipes.ServiceCreatorTool
{
    public class AttributeDescription
    {
        public string QualifiedName { get; }
        public string Value { get; }
        public AttributeDescription(string qualifiedName, string value)
        {
            QualifiedName = qualifiedName;
            Value = value;
        }
    }
}
