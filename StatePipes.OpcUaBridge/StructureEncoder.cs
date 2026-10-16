using Newtonsoft.Json.Linq;
using Opc.Ua;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Encodes a Set command's JSON into a structured value, the inverse of <see cref="StructureDecoder"/>: the
    /// fields in layout order, nested structures inline, arrays as an Int32 length followed by the elements,
    /// wrapped in an ExtensionObject tagged with the DataType's default binary encoding.
    ///
    /// <para>A missing string, ByteString or array field is written as null, which is how the decoder leaves such
    /// a field out, so a Get event's value can be sent back as a Set. Any other missing field is refused rather
    /// than defaulted, and so is a property the structure does not have, so a misspelt field never goes unnoticed.
    /// Scalars follow <see cref="OpcUaValueEncoder"/>'s rules.</para>
    /// </summary>
    internal static class StructureEncoder
    {
        private const int NullLength = -1;
        private static readonly IServiceMessageContext MessageContext = ServiceMessageContext.Create(DefaultTelemetry.Create(_ => { }));

        /// <summary>
        /// Whether values of <paramref name="layout"/> can be written: it and every structure inside it name a
        /// binary encoding, and every other field is of a type <see cref="OpcUaValueEncoder"/> writes.
        /// </summary>
        public static bool CanEncode(StructureLayout layout) => CanEncode(layout, []);

        private static bool CanEncode(StructureLayout layout, HashSet<StructureLayout> visited)
        {
            if (!visited.Add(layout)) return true;
            if (layout.BinaryEncodingId == null) return false;
            return layout.Fields.All(field => field.Structure != null ? CanEncode(field.Structure, visited) : OpcUaValueEncoder.IsSupported(field.BuiltInType));
        }

        public static StatusCode TryEncode(JToken? value, StructureLayout layout, bool isArray, out Variant variant)
        {
            variant = Variant.Null;
            if (!isArray)
            {
                var status = TryEncodeExtension(value, layout, out var extension);
                if (StatusCode.IsBad(status)) return status;
                variant = new Variant(extension);
                return StatusCodes.Good;
            }
            if (value is not JArray array) return StatusCodes.BadTypeMismatch;
            var extensions = new ExtensionObject[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                var status = TryEncodeExtension(array[i], layout, out extensions[i]);
                if (StatusCode.IsBad(status)) return status;
            }
            variant = new Variant((ArrayOf<ExtensionObject>)extensions);
            return StatusCodes.Good;
        }

        private static StatusCode TryEncodeExtension(JToken? value, StructureLayout layout, out ExtensionObject extension)
        {
            extension = default!;
            using BinaryEncoder encoder = new(MessageContext);
            var status = TryWriteStructure(encoder, value, layout);
            if (StatusCode.IsBad(status)) return status;
            extension = new ExtensionObject((ExpandedNodeId)layout.BinaryEncodingId!.Value, (encoder.CloseAndReturnBuffer() ?? []).ToByteString());
            return StatusCodes.Good;
        }

        private static StatusCode TryWriteStructure(BinaryEncoder encoder, JToken? value, StructureLayout layout)
        {
            if (value is not JObject structure) return StatusCodes.BadTypeMismatch;
            if (structure.Properties().Any(property => !layout.Fields.Any(field => field.Name == property.Name))) return StatusCodes.BadTypeMismatch;
            foreach (var field in layout.Fields)
            {
                var fieldValue = structure[field.Name];
                var status = fieldValue == null || fieldValue.Type == JTokenType.Null
                    ? TryWriteNull(encoder, field)
                    : field.IsArray ? TryWriteArray(encoder, fieldValue, field) : TryWriteScalar(encoder, fieldValue, field);
                if (StatusCode.IsBad(status)) return status;
            }
            return StatusCodes.Good;
        }

        /// <summary>Strings, ByteStrings and arrays have a null encoding; every other field must be given.</summary>
        private static StatusCode TryWriteNull(BinaryEncoder encoder, StructureFieldLayout field)
        {
            if (!field.IsArray && (field.Structure != null || field.BuiltInType is not (BuiltInType.String or BuiltInType.ByteString))) return StatusCodes.BadTypeMismatch;
            encoder.WriteInt32(field.Name, NullLength);
            return StatusCodes.Good;
        }

        private static StatusCode TryWriteArray(BinaryEncoder encoder, JToken value, StructureFieldLayout field)
        {
            if (value is not JArray array) return StatusCodes.BadTypeMismatch;
            encoder.WriteInt32(field.Name, array.Count);
            foreach (var element in array)
            {
                var status = TryWriteScalar(encoder, element, field);
                if (StatusCode.IsBad(status)) return status;
            }
            return StatusCodes.Good;
        }

        private static StatusCode TryWriteScalar(BinaryEncoder encoder, JToken value, StructureFieldLayout field)
        {
            if (field.Structure != null) return TryWriteStructure(encoder, value, field.Structure);
            var status = OpcUaValueEncoder.TryConvertScalar(value, field.BuiltInType, out var scalar);
            if (StatusCode.IsBad(status)) return status;
            WriteBuiltIn(encoder, field.Name, field.BuiltInType, scalar!);
            return StatusCodes.Good;
        }

        private static void WriteBuiltIn(BinaryEncoder encoder, string name, BuiltInType builtInType, object value)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean: encoder.WriteBoolean(name, (bool)value); break;
                case BuiltInType.SByte: encoder.WriteSByte(name, (sbyte)value); break;
                case BuiltInType.Byte: encoder.WriteByte(name, (byte)value); break;
                case BuiltInType.Int16: encoder.WriteInt16(name, (short)value); break;
                case BuiltInType.UInt16: encoder.WriteUInt16(name, (ushort)value); break;
                case BuiltInType.Int32 or BuiltInType.Enumeration: encoder.WriteInt32(name, (int)value); break;
                case BuiltInType.UInt32: encoder.WriteUInt32(name, (uint)value); break;
                case BuiltInType.StatusCode: encoder.WriteStatusCode(name, new StatusCode((uint)value)); break;
                case BuiltInType.Int64: encoder.WriteInt64(name, (long)value); break;
                case BuiltInType.UInt64: encoder.WriteUInt64(name, (ulong)value); break;
                case BuiltInType.Float: encoder.WriteFloat(name, (float)value); break;
                case BuiltInType.Double: encoder.WriteDouble(name, (double)value); break;
                case BuiltInType.String: encoder.WriteString(name, (string)value); break;
                case BuiltInType.DateTime: encoder.WriteDateTime(name, new DateTimeUtc((DateTime)value)); break;
                case BuiltInType.Guid: encoder.WriteGuid(name, new Uuid((Guid)value)); break;
                case BuiltInType.ByteString: encoder.WriteByteString(name, ((byte[])value).ToByteString()); break;
                default: throw new ArgumentOutOfRangeException(nameof(builtInType), builtInType, "Not a type the bridge writes");
            }
        }
    }
}
