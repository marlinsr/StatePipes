using Opc.Ua;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Builds a <see cref="StructureLayout"/> for a structure DataType, following nested structure fields down
    /// to built-in types. Layouts are cached per DataType, so a type used in many places has one layout.
    ///
    /// <para>Only plain structures (OPC UA 1.04 DataTypeDefinition, StructureType.Structure) with scalar and
    /// one-dimensional fields are laid out. Anything else -- unions, optional fields, matrices, a concrete
    /// structure field the server does not describe -- cannot be decoded reliably, so the DataType gets no
    /// layout and the variable keeps travelling as a string. Fields of an abstract type are encoded with their
    /// own type information (an ExtensionObject or a Variant) and are published as their fallback types.</para>
    /// </summary>
    /// <param name="describe">Reads a DataType node; returns null when the node cannot be read.</param>
    internal sealed class StructureLayoutBuilder(string exchangeName, Func<NodeId, CancellationToken, Task<DataTypeInfo?>> describe)
    {
        private readonly Dictionary<NodeId, StructureLayout?> _layouts = [];

        /// <summary>The layout of <paramref name="dataTypeId"/>, or null when it cannot be decoded.</summary>
        public async Task<StructureLayout?> BuildAsync(NodeId dataTypeId, CancellationToken ct)
        {
            List<NodeId> started = [];
            var layout = await BuildStructureAsync(dataTypeId, started, ct);
            if (layout != null) return layout;
            // Layouts started on the way to the failure may point at a structure that has none; forget them so
            // each is judged again on its own.
            foreach (var id in started)
                if (_layouts.TryGetValue(id, out var startedLayout) && startedLayout != null) _layouts.Remove(id);
            return null;
        }

        private async Task<StructureLayout?> BuildStructureAsync(NodeId dataTypeId, List<NodeId> started, CancellationToken ct)
        {
            if (_layouts.TryGetValue(dataTypeId, out var known)) return known;
            var info = await describe(dataTypeId, ct);
            if (info?.Definition is not { StructureType: StructureType.Structure } definition)
            {
                _layouts[dataTypeId] = null;
                return null;
            }
            var (@namespace, name) = OpcUaNaming.ToStructureTypeNames(exchangeName, dataTypeId);
            StructureLayout layout = new(dataTypeId, @namespace, name, definition.DefaultEncodingId.IsNull ? null : definition.DefaultEncodingId);
            _layouts[dataTypeId] = layout;
            started.Add(dataTypeId);
            HashSet<string> fieldNames = new(StringComparer.Ordinal);
            foreach (var field in definition.Fields.ToList())
            {
                var fieldLayout = await BuildFieldAsync(field, UniqueFieldName(field.Name, fieldNames), started, ct);
                if (fieldLayout == null)
                {
                    _layouts[dataTypeId] = null;
                    return null;
                }
                layout.AddField(fieldLayout);
            }
            return layout;
        }

        private async Task<StructureFieldLayout?> BuildFieldAsync(StructureField field, string name, List<NodeId> started, CancellationToken ct)
        {
            bool isArray;
            if (field.ValueRank == ValueRanks.Scalar) isArray = false;
            else if (field.ValueRank == ValueRanks.OneDimension) isArray = true;
            else return null;
            var info = await describe(field.DataType, ct);
            if (info == null) return null;
            if (info.BuiltInType == BuiltInType.ExtensionObject && !info.IsAbstract)
            {
                var nested = await BuildStructureAsync(field.DataType, started, ct);
                return nested == null ? null : new(name, BuiltInType.ExtensionObject, null, nested, isArray);
            }
            var builtInType = info.BuiltInType == BuiltInType.Null ? BuiltInType.Variant : info.BuiltInType;
            return new(name, builtInType, OpcUaValueMapper.MapToClrType(builtInType, ValueRanks.Scalar), null, isArray);
        }

        private static string UniqueFieldName(string? fieldName, HashSet<string> taken)
        {
            var name = string.IsNullOrEmpty(fieldName) ? "_" : OpcUaNaming.Sanitize(fieldName);
            var unique = name;
            for (var suffix = 2; !taken.Add(unique); suffix++) unique = $"{name}_{suffix}";
            return unique;
        }
    }
}
