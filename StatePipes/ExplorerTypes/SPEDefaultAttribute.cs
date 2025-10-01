namespace StatePipes.ExplorerTypes
{
    public class SPEDefaultAttribute(string defaultValue) : Attribute
    {
        public string DefaultValue { get; } = defaultValue;
    }
}
