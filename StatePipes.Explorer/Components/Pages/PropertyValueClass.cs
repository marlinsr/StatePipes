namespace StatePipes.Explorer.Components.Pages
{
    public class PropertyValueClass
    {
        public Guid InstanceGuid { get; }
        public string CommandTypeFullName { get; }
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
        public object? Value { get; }
        public PropertyValueType PropertyTypeEnum { get; }

        public Type? PropertyType { get; }

        public List<string> EnumValueList { get; } = new();

        public bool Nullable { get; }

        public string? Name { get; }

        public bool IsFromEvent { get; }

        public PropertyValueClass(Guid instanceGuid, string commandTypeFullName, string? name, object? value, PropertyValueType propertyTypeEnum, Type? propertyType, bool nullable, bool isFromEvent = false)
        {
            InstanceGuid = instanceGuid;
            CommandTypeFullName = commandTypeFullName;
            Name = name;
            Value = value;
            PropertyTypeEnum = propertyTypeEnum;
            PropertyType = propertyType;
            Nullable = nullable;
            IsFromEvent = isFromEvent;
        }

        public PropertyValueClass(Guid instanceGuid, string commandTypeFullName, string? name, object? value, List<string> enumValuesList, bool nullable, bool isFromEvent = false) :
            this(instanceGuid, commandTypeFullName, name, value, PropertyValueType.Enum, null, nullable, isFromEvent) 
        {
            EnumValueList = enumValuesList;
        }
    }
}
