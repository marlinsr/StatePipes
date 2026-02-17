namespace StatePipes.Messages
{
    public class AttributeDescription(string fullName, string value)
    {
        public string FullName { get; } = fullName;
        public string Value { get; } = value;
    }
}
