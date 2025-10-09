namespace StatePipes.ServiceCreatorTool
{
    public class TypeDescription : TypeNames
    {
        public TypeDescription(string qualifiedName, string fullName, string assemblyName, string @namespace, string arrayFullName, int arrayRank, List<AttributeDescription> attributes, List<ParameterDescription> properties, bool isEvent, bool isCommand, bool isAttribute, List<EnumValue> enumValues, TypeNames? genericNames, TypeNames[] genericArgumentsNames)
        {
            QualifiedName = qualifiedName;
            FullName = fullName;
            AssemblyName = assemblyName;
            Namespace = @namespace;
            ArrayFullName = arrayFullName;
            ArrayRank = arrayRank;
            Attributes = attributes;
            Properties = properties;
            IsEvent = isEvent;
            IsCommand = isCommand;
            IsAttribute = isAttribute;
            EnumValues = enumValues;
            GenericNames = genericNames;
            GenericArgumentsNames = genericArgumentsNames;
        }

        public TypeDescription() { }

        public string ArrayFullName { get; set; } = string.Empty;
        public int ArrayRank { get; set; }
        public List<AttributeDescription> Attributes { get; set; } = [];
        public List<ParameterDescription> Properties { get; set; } = [];
        public bool IsEvent { get; set; }
        public bool IsCommand { get; set; }
        public bool IsAttribute { get; set; }
        public List<EnumValue> EnumValues { get; set; } = [];
        public TypeNames? GenericNames { get; set; }
        public TypeNames[] GenericArgumentsNames { get; set; } = [];
        public bool IsGeneric() { return GenericNames != null; }
        public bool IsArray() { return !string.IsNullOrEmpty(ArrayFullName); }
        public bool IsEnum() { return EnumValues.Any(); }
    }
}
