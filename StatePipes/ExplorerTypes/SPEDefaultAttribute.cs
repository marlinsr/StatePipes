namespace StatePipes.ExplorerTypes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Enum | AttributeTargets.Struct)]
    public class SPEDefaultAttribute(string defaultValue) : Attribute
    {
        public string DefaultValue { get; } = defaultValue;
    }
}
