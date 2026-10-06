using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Opc.Ua;
using System.Globalization;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Decides what CLR type a variable is published as, and converts read values into it.
    ///
    /// <para>Scalars and one-dimensional arrays of the numeric, boolean, string, date, guid and byte string
    /// built-in types keep their natural type, so a client gets a <c>double</c> for a Double variable. Anything
    /// without a faithful plain type -- structures, NodeIds, localized text, matrices, variables whose rank or
    /// type is not fixed -- is published as a string holding its text or JSON form.</para>
    /// </summary>
    internal static class OpcUaValueMapper
    {
        private static readonly JsonSerializerSettings FallbackJsonSettings = new()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            MaxDepth = 16,
            Error = (_, e) => e.ErrorContext.Handled = true
        };

        public static Type MapToClrType(BuiltInType builtInType, int valueRank)
        {
            var scalar = MapScalar(builtInType);
            return (scalar, valueRank) switch
            {
                (null, _) => typeof(string),
                (_, ValueRanks.Scalar) => scalar,
                (_, ValueRanks.OneDimension) => scalar.MakeArrayType(),
                _ => typeof(string)
            };
        }

        /// <summary>The natural CLR type for a built-in type, or null when it has none and travels as text.</summary>
        private static Type? MapScalar(BuiltInType builtInType) => builtInType switch
        {
            BuiltInType.Boolean => typeof(bool),
            BuiltInType.SByte => typeof(sbyte),
            BuiltInType.Byte => typeof(byte),
            BuiltInType.Int16 => typeof(short),
            BuiltInType.UInt16 => typeof(ushort),
            BuiltInType.Int32 => typeof(int),
            BuiltInType.UInt32 => typeof(uint),
            BuiltInType.Int64 => typeof(long),
            BuiltInType.UInt64 => typeof(ulong),
            BuiltInType.Float => typeof(float),
            BuiltInType.Double => typeof(double),
            BuiltInType.String => typeof(string),
            BuiltInType.DateTime => typeof(DateTime),
            BuiltInType.Guid => typeof(Guid),
            BuiltInType.ByteString => typeof(byte[]),
            BuiltInType.StatusCode => typeof(uint),
            BuiltInType.Enumeration => typeof(int),
            BuiltInType.Integer => typeof(long),
            BuiltInType.UInteger => typeof(ulong),
            BuiltInType.Number => typeof(double),
            _ => null
        };

        /// <summary>
        /// Converts a value read from the server into JSON shaped as <paramref name="targetType"/>. Returns a JSON
        /// null when the value is absent or cannot be represented, so a client never receives a value that fails
        /// to deserialize into the type the bridge advertised.
        /// </summary>
        public static JToken ToJToken(object? value, Type targetType)
        {
            if (value is Variant variant) value = variant.Value;
            if (value == null) return JValue.CreateNull();
            if (targetType == typeof(string)) return new JValue(ToText(value));
            if (targetType == typeof(byte[])) return value is byte[] bytes ? new JValue(bytes) : JValue.CreateNull();
            if (targetType.IsArray)
            {
                if (value is not Array array) return JValue.CreateNull();
                var elementType = targetType.GetElementType()!;
                JArray result = [];
                foreach (var element in array) result.Add(ToJToken(element, elementType));
                return result;
            }
            var converted = ConvertScalar(value, targetType);
            return converted == null ? JValue.CreateNull() : JToken.FromObject(converted);
        }

        private static object? ConvertScalar(object value, Type targetType)
        {
            try
            {
                value = value switch
                {
                    StatusCode statusCode => statusCode.Code,
                    Uuid uuid => (Guid)uuid,
                    _ => value
                };
                if (targetType.IsInstanceOfType(value)) return value;
                if (targetType == typeof(Guid)) return value is string s && Guid.TryParse(s, out var guid) ? guid : null;
                if (targetType == typeof(DateTime)) return value is DateTime dt ? dt : null;
                return System.Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static string ToText(object value) => value switch
        {
            string s => s,
            LocalizedText text => text.Text ?? string.Empty,
            NodeId or ExpandedNodeId or QualifiedName or StatusCode or System.Xml.XmlElement => value.ToString() ?? string.Empty,
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable when value.GetType().IsPrimitive => formattable.ToString(null, CultureInfo.InvariantCulture),
            ExtensionObject { Body: not null } extension => ToJson(extension.Body),
            _ => ToJson(value)
        };

        private static string ToJson(object value)
        {
            try
            {
                return JsonConvert.SerializeObject(value, Formatting.None, FallbackJsonSettings);
            }
            catch
            {
                return value.ToString() ?? string.Empty;
            }
        }
    }
}
