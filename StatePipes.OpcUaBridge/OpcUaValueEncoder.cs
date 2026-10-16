using Newtonsoft.Json.Linq;
using Opc.Ua;
using System.Globalization;
using System.Numerics;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Turns the JSON value of a Set command into a <see cref="Variant"/> of the variable's exact built-in type, the
    /// inverse of <see cref="OpcUaValueMapper"/>. Servers refuse an Int32 written to an Int16 variable, so the
    /// Variant always carries the declared type, never one inferred from the JSON.
    ///
    /// <para>Conversion is strict: a value is never rounded, wrapped or parsed from text of the wrong kind,
    /// because a setpoint that silently changed on the way is worse than one refused. JSON of the wrong kind is
    /// <c>BadTypeMismatch</c>; a number the type cannot hold is <c>BadOutOfRange</c>. A DateTime without an offset
    /// is taken as UTC, the zone OPC UA keeps every DateTime in.</para>
    /// </summary>
    internal static class OpcUaValueEncoder
    {
        /// <summary>Whether a value of <paramref name="builtInType"/> can be written: the concrete types with a natural CLR form.</summary>
        public static bool IsSupported(BuiltInType builtInType) => builtInType is BuiltInType.Boolean or BuiltInType.SByte or BuiltInType.Byte
            or BuiltInType.Int16 or BuiltInType.UInt16 or BuiltInType.Int32 or BuiltInType.UInt32 or BuiltInType.Int64 or BuiltInType.UInt64
            or BuiltInType.Float or BuiltInType.Double or BuiltInType.String or BuiltInType.DateTime or BuiltInType.Guid
            or BuiltInType.ByteString or BuiltInType.StatusCode or BuiltInType.Enumeration;

        public static StatusCode TryEncode(JToken? value, BuiltInType builtInType, bool isArray, out Variant variant)
        {
            variant = Variant.Null;
            if (value == null || value.Type == JTokenType.Null) return StatusCodes.BadTypeMismatch;
            if (!isArray)
            {
                var status = TryConvertScalar(value, builtInType, out var scalar);
                if (StatusCode.IsBad(status)) return status;
                variant = ToVariant(scalar!, builtInType);
                return StatusCodes.Good;
            }
            if (value is not JArray array) return StatusCodes.BadTypeMismatch;
            var elements = new object[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                var status = TryConvertScalar(array[i], builtInType, out var element);
                if (StatusCode.IsBad(status)) return status;
                elements[i] = element!;
            }
            variant = ToArrayVariant(elements, builtInType);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Converts one JSON value into the CLR value a variable of <paramref name="builtInType"/> is published as:
        /// a DateTime in UTC, a Guid, a byte[], a uint for a StatusCode, an int for an Enumeration.
        /// </summary>
        public static StatusCode TryConvertScalar(JToken value, BuiltInType builtInType, out object? clrValue)
        {
            clrValue = null;
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    if (value.Type != JTokenType.Boolean) return StatusCodes.BadTypeMismatch;
                    clrValue = (bool)value;
                    return StatusCodes.Good;
                case BuiltInType.SByte: return TryConvertInteger(value, sbyte.MinValue, sbyte.MaxValue, i => (sbyte)i, out clrValue);
                case BuiltInType.Byte: return TryConvertInteger(value, byte.MinValue, byte.MaxValue, i => (byte)i, out clrValue);
                case BuiltInType.Int16: return TryConvertInteger(value, short.MinValue, short.MaxValue, i => (short)i, out clrValue);
                case BuiltInType.UInt16: return TryConvertInteger(value, ushort.MinValue, ushort.MaxValue, i => (ushort)i, out clrValue);
                case BuiltInType.Int32:
                case BuiltInType.Enumeration: return TryConvertInteger(value, int.MinValue, int.MaxValue, i => (int)i, out clrValue);
                case BuiltInType.UInt32:
                case BuiltInType.StatusCode: return TryConvertInteger(value, uint.MinValue, uint.MaxValue, i => (uint)i, out clrValue);
                case BuiltInType.Int64: return TryConvertInteger(value, long.MinValue, long.MaxValue, i => (long)i, out clrValue);
                case BuiltInType.UInt64: return TryConvertInteger(value, ulong.MinValue, ulong.MaxValue, i => (ulong)i, out clrValue);
                case BuiltInType.Float:
                case BuiltInType.Double:
                    return TryConvertReal(value, builtInType == BuiltInType.Float, out clrValue);
                case BuiltInType.String:
                    if (value.Type != JTokenType.String) return StatusCodes.BadTypeMismatch;
                    clrValue = (string)value!;
                    return StatusCodes.Good;
                case BuiltInType.DateTime:
                    if (!TryParseDateTime(value, out var dateTime)) return StatusCodes.BadTypeMismatch;
                    clrValue = dateTime;
                    return StatusCodes.Good;
                case BuiltInType.Guid:
                    if (value.Type != JTokenType.String || !Guid.TryParse((string)value!, out var guid)) return StatusCodes.BadTypeMismatch;
                    clrValue = guid;
                    return StatusCodes.Good;
                case BuiltInType.ByteString:
                    if (value.Type != JTokenType.String) return StatusCodes.BadTypeMismatch;
                    var text = (string)value!;
                    var bytes = new byte[text.Length];
                    if (!Convert.TryFromBase64String(text, bytes, out var length)) return StatusCodes.BadTypeMismatch;
                    clrValue = bytes[..length];
                    return StatusCodes.Good;
                default:
                    return StatusCodes.BadTypeMismatch;
            }
        }

        private static StatusCode TryConvertInteger(JToken value, BigInteger min, BigInteger max, Func<BigInteger, object> convert, out object? clrValue)
        {
            clrValue = null;
            if (value.Type != JTokenType.Integer || value is not JValue { Value: { } raw }) return StatusCodes.BadTypeMismatch;
            var integer = raw is BigInteger big ? big : new BigInteger(Convert.ToInt64(raw, CultureInfo.InvariantCulture));
            if (integer < min || integer > max) return StatusCodes.BadOutOfRange;
            clrValue = convert(integer);
            return StatusCodes.Good;
        }

        private static StatusCode TryConvertReal(JToken value, bool isFloat, out object? clrValue)
        {
            clrValue = null;
            if (value.Type is not (JTokenType.Integer or JTokenType.Float) || value is not JValue { Value: { } raw }) return StatusCodes.BadTypeMismatch;
            var real = raw is BigInteger big ? (double)big : Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            if (double.IsNaN(real)) return StatusCodes.BadTypeMismatch;
            if (double.IsInfinity(real) || (isFloat && Math.Abs(real) > float.MaxValue)) return StatusCodes.BadOutOfRange;
            clrValue = isFloat ? (object)(float)real : real;
            return StatusCodes.Good;
        }

        private static bool TryParseDateTime(JToken value, out DateTime dateTime)
        {
            switch (value.Type)
            {
                case JTokenType.String:
                    return DateTime.TryParse((string)value!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dateTime);
                case JTokenType.Date:
                    var date = (DateTime)value;
                    dateTime = date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : date.ToUniversalTime();
                    return true;
                default:
                    dateTime = default;
                    return false;
            }
        }

        private static Variant ToVariant(object value, BuiltInType builtInType) => builtInType switch
        {
            BuiltInType.Boolean => new Variant((bool)value),
            BuiltInType.SByte => new Variant((sbyte)value),
            BuiltInType.Byte => new Variant((byte)value),
            BuiltInType.Int16 => new Variant((short)value),
            BuiltInType.UInt16 => new Variant((ushort)value),
            BuiltInType.Int32 or BuiltInType.Enumeration => new Variant((int)value),
            BuiltInType.UInt32 => new Variant((uint)value),
            BuiltInType.StatusCode => new Variant(new StatusCode((uint)value)),
            BuiltInType.Int64 => new Variant((long)value),
            BuiltInType.UInt64 => new Variant((ulong)value),
            BuiltInType.Float => new Variant((float)value),
            BuiltInType.Double => new Variant((double)value),
            BuiltInType.String => new Variant((string)value),
            BuiltInType.DateTime => new Variant(new DateTimeUtc((DateTime)value)),
            BuiltInType.Guid => new Variant(new Uuid((Guid)value)),
            BuiltInType.ByteString => new Variant(((byte[])value).ToByteString()),
            _ => throw new ArgumentOutOfRangeException(nameof(builtInType), builtInType, "Not a type the bridge writes")
        };

        private static Variant ToArrayVariant(object[] values, BuiltInType builtInType) => builtInType switch
        {
            BuiltInType.Boolean => new Variant(Typed<bool>(values)),
            BuiltInType.SByte => new Variant(Typed<sbyte>(values)),
            BuiltInType.Byte => new Variant(Typed<byte>(values)),
            BuiltInType.Int16 => new Variant(Typed<short>(values)),
            BuiltInType.UInt16 => new Variant(Typed<ushort>(values)),
            BuiltInType.Int32 or BuiltInType.Enumeration => new Variant(Typed<int>(values)),
            BuiltInType.UInt32 => new Variant(Typed<uint>(values)),
            BuiltInType.StatusCode => new Variant((ArrayOf<StatusCode>)values.Select(v => new StatusCode((uint)v)).ToArray()),
            BuiltInType.Int64 => new Variant(Typed<long>(values)),
            BuiltInType.UInt64 => new Variant(Typed<ulong>(values)),
            BuiltInType.Float => new Variant(Typed<float>(values)),
            BuiltInType.Double => new Variant(Typed<double>(values)),
            BuiltInType.String => new Variant(Typed<string>(values)),
            BuiltInType.DateTime => new Variant((ArrayOf<DateTimeUtc>)values.Select(v => new DateTimeUtc((DateTime)v)).ToArray()),
            BuiltInType.Guid => new Variant((ArrayOf<Uuid>)values.Select(v => new Uuid((Guid)v)).ToArray()),
            BuiltInType.ByteString => new Variant((ArrayOf<ByteString>)values.Select(v => ((byte[])v).ToByteString()).ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(builtInType), builtInType, "Not a type the bridge writes")
        };

        private static ArrayOf<T> Typed<T>(object[] values) => values.Cast<T>().ToArray();
    }
}
