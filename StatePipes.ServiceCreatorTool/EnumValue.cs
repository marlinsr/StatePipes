namespace StatePipes.ServiceCreatorTool
{
    public class EnumValue(string name, int value)
    {
        public string Name { get; } = name;
        public int Value { get; } = value;
    }
}
