namespace StatePipes.Explorer.Components.Pages
{
    public class PropertyValueClass(Guid instanceGuid, string commandTypeFullName, string? name, object? value, PropertyValueClass.PropertyValueType propertyTypeEnum, Type? propertyType, bool nullable, bool isFromEvent = false)
    {
        public Guid InstanceGuid { get; } = instanceGuid;
        public string CommandTypeFullName { get; } = commandTypeFullName;
        public enum PropertyValueType
        {
            Primitive,
            Bool,
            Enum,
            Class,
            String,
            Guid,
            DateTime,
            DateTimeOffset,
            PrimitiveArray,
            Array,
            Dictionary,
            DictionaryEntry,
            ByteArray,
            Decimal,
            Image,
            LineChart

        }
        public object? Value { get; } = value;
        public PropertyValueType PropertyTypeEnum { get; } = propertyTypeEnum;

        public Type? PropertyType { get; } = propertyType;

        public List<string> EnumValueList { get; } = [];

        public bool Nullable { get; } = nullable;

        public string? Name { get; } = name;

        public bool IsFromEvent { get; } = isFromEvent;

        public PropertyValueClass(Guid instanceGuid, string commandTypeFullName, string? name, object? value, List<string> enumValuesList, bool nullable, bool isFromEvent = false) :
            this(instanceGuid, commandTypeFullName, name, value, PropertyValueType.Enum, null, nullable, isFromEvent) 
        {
            EnumValueList = enumValuesList;
        }
    }
}
