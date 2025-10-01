namespace StatePipes.ServiceCreatorTool
{
    public class EnumValue
    {
        public EnumValue(string name, int value)
        {
            Name = name;
            Value = value;
        }
        public string Name { get; }
        public int Value { get; }
    }
}
