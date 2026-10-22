using Newtonsoft.Json.Linq;
using Opc.Ua;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Decodes structured values into JSON shaped as their <see cref="StructureLayout"/>.
    ///
    /// <para>The bridge's session has no generated classes for a server's structures, so a structured value
    /// arrives as an ExtensionObject holding the raw binary encoding. That encoding is the fields in order, with
    /// nested structures inline and arrays as an Int32 length followed by the elements, so the layout is all
    /// that is needed to read it.</para>
    ///
    /// <para>A body that is not binary, ends early or has bytes left over does not match the layout, and comes
    /// back as a JSON null rather than a value that would mislead.</para>
    /// </summary>
    internal static class StructureDecoder
    {
        private static readonly IServiceMessageContext MessageContext = ServiceMessageContext.Create(DefaultTelemetry.Create(_ => { }));

        public static JToken ToJToken(object? value, StructureLayout layout, bool isArray)
        {
            if (value is Variant variant) value = variant.AsBoxedObject(Variant.BoxingBehavior.Legacy);
            if (!isArray) return value is ExtensionObject extension ? Decode(extension, layout) : JValue.CreateNull();
            if (value is not Array array) return JValue.CreateNull();
            JArray result = [];
            foreach (var element in array)
            {
                var decoded = element is ExtensionObject extension ? Decode(extension, layout) : JValue.CreateNull();
                if (decoded.Type == JTokenType.Null) return JValue.CreateNull();
                result.Add(decoded);
            }
            return result;
        }

        private static JToken Decode(ExtensionObject extension, StructureLayout layout)
        {
            if (!extension.TryGetAsBinary(out ByteString encoded, MessageContext))
            {
                Log?.LogWarning($"Cannot decode {layout.FullName}: the value is not binary encoded");
                return JValue.CreateNull();
            }
            var body = encoded.ToArray();
            try
            {
                using BinaryDecoder decoder = new(body, MessageContext);
                var result = ReadStructure(decoder, layout);
                if (decoder.Position == body.Length) return result;
                Log?.LogWarning($"Cannot decode {layout.FullName}: {body.Length - decoder.Position} bytes left over");
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"Cannot decode {layout.FullName}: {ex.Message}");
            }
            return JValue.CreateNull();
        }

        /// <summary>Null fields are left out, as the Get event leaves out a null Value.</summary>
        private static JObject ReadStructure(BinaryDecoder decoder, StructureLayout layout)
        {
            JObject result = [];
            foreach (var field in layout.Fields)
            {
                var value = field.IsArray ? ReadArray(decoder, field) : ReadScalar(decoder, field);
                if (value.Type != JTokenType.Null) result[field.Name] = value;
            }
            return result;
        }

        private static JToken ReadArray(BinaryDecoder decoder, StructureFieldLayout field)
        {
            var length = decoder.ReadInt32(field.Name);
            if (length < 0) return JValue.CreateNull();
            if (MessageContext.MaxArrayLength > 0 && length > MessageContext.MaxArrayLength)
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, $"{field.Name} claims {length} elements");
            JArray result = [];
            for (var i = 0; i < length; i++) result.Add(ReadScalar(decoder, field));
            return result;
        }

        private static JToken ReadScalar(BinaryDecoder decoder, StructureFieldLayout field) => field.Structure != null
            ? ReadStructure(decoder, field.Structure)
            : OpcUaValueMapper.ToJToken(ReadBuiltIn(decoder, field.Name, field.BuiltInType), field.ClrType!);

        private static object? ReadBuiltIn(BinaryDecoder decoder, string name, BuiltInType builtInType) => builtInType switch
        {
            BuiltInType.Boolean => decoder.ReadBoolean(name),
            BuiltInType.SByte => decoder.ReadSByte(name),
            BuiltInType.Byte => decoder.ReadByte(name),
            BuiltInType.Int16 => decoder.ReadInt16(name),
            BuiltInType.UInt16 => decoder.ReadUInt16(name),
            BuiltInType.Int32 or BuiltInType.Enumeration => decoder.ReadInt32(name),
            BuiltInType.UInt32 => decoder.ReadUInt32(name),
            BuiltInType.Int64 => decoder.ReadInt64(name),
            BuiltInType.UInt64 => decoder.ReadUInt64(name),
            BuiltInType.Float => decoder.ReadFloat(name),
            BuiltInType.Double => decoder.ReadDouble(name),
            BuiltInType.String => decoder.ReadString(name),
            BuiltInType.DateTime => decoder.ReadDateTime(name),
            BuiltInType.Guid => decoder.ReadGuid(name),
            BuiltInType.ByteString => decoder.ReadByteString(name),
            BuiltInType.XmlElement => decoder.ReadXmlElement(name),
            BuiltInType.NodeId => decoder.ReadNodeId(name),
            BuiltInType.ExpandedNodeId => decoder.ReadExpandedNodeId(name),
            BuiltInType.StatusCode => decoder.ReadStatusCode(name),
            BuiltInType.QualifiedName => decoder.ReadQualifiedName(name),
            BuiltInType.LocalizedText => decoder.ReadLocalizedText(name),
            BuiltInType.ExtensionObject => decoder.ReadExtensionObject(name),
            BuiltInType.DataValue => decoder.ReadDataValue(name),
            BuiltInType.DiagnosticInfo => decoder.ReadDiagnosticInfo(name),
            _ => decoder.ReadVariant(name) // Variant, and the abstract Number, Integer and UInteger
        };
    }
}
