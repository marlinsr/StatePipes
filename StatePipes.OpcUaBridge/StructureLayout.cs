using Opc.Ua;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// The fields of an OPC UA structure DataType in encoding order, as read from its StructureDefinition. The
    /// bridge publishes a structured value as a type named after the DataType (see
    /// <see cref="OpcUaNaming.ToStructureTypeNames"/>) and decodes the binary body field by field with it.
    ///
    /// <para>Fields are added after the layout is registered, so a structure that holds an array of itself
    /// points back at its own layout.</para>
    /// </summary>
    /// <param name="binaryEncodingId">The DataType's default binary encoding, which a written value is tagged with; null when the server names none.</param>
    internal sealed class StructureLayout(NodeId dataTypeId, string @namespace, string name, NodeId? binaryEncodingId = null)
    {
        private readonly List<StructureFieldLayout> _fields = [];

        public NodeId DataTypeId { get; } = dataTypeId;
        public string Namespace { get; } = @namespace;
        public string Name { get; } = name;
        public NodeId? BinaryEncodingId { get; } = binaryEncodingId;
        public string FullName => $"{Namespace}.{Name}";
        public IReadOnlyList<StructureFieldLayout> Fields => _fields;

        public void AddField(StructureFieldLayout field) => _fields.Add(field);
    }

    /// <summary>
    /// One field of a <see cref="StructureLayout"/>: either a nested structure, or a built-in value published
    /// as <paramref name="ClrType"/> like a variable of that type would be.
    /// </summary>
    /// <param name="Name">The field name, made safe for a property name.</param>
    /// <param name="BuiltInType">How the field is encoded; Variant for abstract types encoded as one.</param>
    /// <param name="ClrType">The type of one element of a built-in field; null for a nested structure.</param>
    /// <param name="Structure">The nested structure's layout; null for a built-in field.</param>
    /// <param name="IsArray">Whether the field is a one-dimensional array of the above.</param>
    internal sealed record StructureFieldLayout(string Name, BuiltInType BuiltInType, Type? ClrType, StructureLayout? Structure, bool IsArray);

    /// <summary>What <see cref="StructureLayoutBuilder"/> needs to know about a DataType node.</summary>
    /// <param name="Definition">The StructureDefinition from the DataTypeDefinition attribute, if it has one.</param>
    internal sealed record DataTypeInfo(BuiltInType BuiltInType, bool IsAbstract, StructureDefinition? Definition);
}
