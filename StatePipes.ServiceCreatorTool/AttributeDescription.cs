namespace StatePipes.ServiceCreatorTool
{
    public class AttributeDescription
    {
        public string FullName { get; }
        public string Value { get; }
        public AttributeDescription(string fullName, string value)
        {
            FullName = fullName;
            Value = value;
        }
    }
}
