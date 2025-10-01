using Newtonsoft.Json;

namespace StatePipes.Messages
{
    public class TypeDescription : TypeNames
    {
        [JsonConstructor]
        public TypeDescription(string qualifiedName, string fullName, string assemblyName, string @namespace, string arrayQualifiedName, int arrayRank, List<AttributeDescription> attributes, List<ParameterDescription> properties, bool isEvent, bool isCommand, bool isAttribute, List<EnumValue> enumValues, TypeNames? genericNames, TypeNames[] genericArgumentsNames)
        {
            QualifiedName = qualifiedName;
            FullName = fullName;
            AssemblyName = assemblyName;
            Namespace = @namespace;
            ArrayQualifiedName = arrayQualifiedName;
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

        public string ArrayQualifiedName { get; set; } = string.Empty;
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
        public bool IsArray() { return !string.IsNullOrEmpty(ArrayQualifiedName); }
        public bool IsEnum() { return EnumValues.Any(); }
    }
}
