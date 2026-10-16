using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.OpcUaBridge;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>The strict conversion of a Set command's JSON Value into a Variant of the variable's exact type.</summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class OpcUaValueEncoderTest
    {
        /// <summary>Parses like the bridge does, leaving dates as strings.</summary>
        private static JToken Parse(string json)
        {
            using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(json)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
            return JToken.Load(reader);
        }

        private static Variant Encode(string json, BuiltInType builtInType, bool isArray = false)
        {
            var status = OpcUaValueEncoder.TryEncode(Parse(json), builtInType, isArray, out var variant);
            Assert.AreEqual(StatusCodes.Good, status.Code, $"{json} as {builtInType}");
            Assert.AreEqual(builtInType == BuiltInType.Enumeration ? BuiltInType.Int32 : builtInType, variant.TypeInfo.BuiltInType);
            Assert.AreEqual(isArray ? ValueRanks.OneDimension : ValueRanks.Scalar, variant.TypeInfo.ValueRank);
            return variant;
        }

        private static StatusCode Refuse(string? json, BuiltInType builtInType, bool isArray = false) =>
            OpcUaValueEncoder.TryEncode(json == null ? null : Parse(json), builtInType, isArray, out _);

        private static object? Boxed(Variant variant) => variant.AsBoxedObject(Variant.BoxingBehavior.Legacy);

        [TestMethod]
        [DataRow("true", BuiltInType.Boolean, true)]
        [DataRow("-5", BuiltInType.SByte, (sbyte)-5)]
        [DataRow("5", BuiltInType.Byte, (byte)5)]
        [DataRow("-7", BuiltInType.Int16, (short)-7)]
        [DataRow("7", BuiltInType.UInt16, (ushort)7)]
        [DataRow("-70000", BuiltInType.Int32, -70000)]
        [DataRow("70000", BuiltInType.UInt32, 70000u)]
        [DataRow("-5000000000", BuiltInType.Int64, -5000000000L)]
        [DataRow("18446744073709551615", BuiltInType.UInt64, ulong.MaxValue)]
        [DataRow("1.5", BuiltInType.Float, 1.5f)]
        [DataRow("1.25", BuiltInType.Double, 1.25)]
        [DataRow("\"abc\"", BuiltInType.String, "abc")]
        [DataRow("12", BuiltInType.Enumeration, 12)]
        public void Encode_EachNaturalType_CarriesExactBuiltInType(string json, BuiltInType builtInType, object expected) =>
            Assert.AreEqual(expected, Boxed(Encode(json, builtInType)));

        [TestMethod]
        public void Encode_ByteStringGuidAndStatusCode_CarryExactBuiltInType()
        {
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ((ByteString)Boxed(Encode("\"AQID\"", BuiltInType.ByteString))!).ToArray());
            var guid = Guid.NewGuid();
            Assert.AreEqual(guid, (Guid)(Uuid)Boxed(Encode($"\"{guid}\"", BuiltInType.Guid))!);
            Assert.AreEqual(StatusCodes.BadOutOfRange, ((StatusCode)Boxed(Encode(((StatusCode)StatusCodes.BadOutOfRange).Code.ToString(System.Globalization.CultureInfo.InvariantCulture), BuiltInType.StatusCode))!).Code);
        }

        [TestMethod]
        public void Encode_IntegerJsonForDouble_Accepted()
        {
            Assert.AreEqual(5.0, Boxed(Encode("5", BuiltInType.Double)));
            Assert.AreEqual(5.0f, Boxed(Encode("5", BuiltInType.Float)));
        }

        [TestMethod]
        [DataRow("3.7", BuiltInType.Int16)]
        [DataRow("3.0", BuiltInType.Int32)]
        public void Encode_FractionForInteger_TypeMismatch(string json, BuiltInType builtInType) =>
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse(json, builtInType).Code);

        [TestMethod]
        [DataRow("70000", BuiltInType.Int16)]
        [DataRow("-1", BuiltInType.UInt32)]
        [DataRow("300", BuiltInType.Byte)]
        [DataRow("18446744073709551616", BuiltInType.UInt64)]
        [DataRow("1e39", BuiltInType.Float)]
        public void Encode_OutOfRange_BadOutOfRange(string json, BuiltInType builtInType) =>
            Assert.AreEqual(StatusCodes.BadOutOfRange, Refuse(json, builtInType).Code);

        [TestMethod]
        [DataRow("\"5\"", BuiltInType.Double)]
        [DataRow("{}", BuiltInType.Double)]
        [DataRow("[1]", BuiltInType.Double)]
        [DataRow("5", BuiltInType.String)]
        [DataRow("1", BuiltInType.Boolean)]
        [DataRow("\"not a guid\"", BuiltInType.Guid)]
        [DataRow("\"not base64!\"", BuiltInType.ByteString)]
        [DataRow("\"yesterday\"", BuiltInType.DateTime)]
        [DataRow("\"x\"", BuiltInType.NodeId)]
        public void Encode_WrongJsonKind_TypeMismatch(string json, BuiltInType builtInType) =>
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse(json, builtInType).Code);

        [TestMethod]
        public void Encode_NullOrMissing_TypeMismatch()
        {
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse(null, BuiltInType.Double).Code);
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse("null", BuiltInType.Double).Code);
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse("null", BuiltInType.String).Code);
        }

        [TestMethod]
        public void Encode_DateTimeWithOffset_ConvertedToUtc()
        {
            var value = ((DateTimeUtc)Boxed(Encode("\"2026-01-01T02:00:00+02:00\"", BuiltInType.DateTime))!).ToDateTime();
            Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), value);
        }

        [TestMethod]
        public void Encode_DateTimeWithoutOffset_TreatedAsUtc()
        {
            var value = ((DateTimeUtc)Boxed(Encode("\"2026-01-01T00:00:00\"", BuiltInType.DateTime))!).ToDateTime();
            Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), value);
            Assert.AreEqual(DateTimeKind.Utc, value.Kind);
        }

        [TestMethod]
        public void Encode_Arrays_TypedArrayVariant()
        {
            CollectionAssert.AreEqual(new[] { 1.5, 2.0 }, (double[])Boxed(Encode("[1.5, 2]", BuiltInType.Double, isArray: true))!);
            CollectionAssert.AreEqual(new short[] { 3, -4 }, (short[])Boxed(Encode("[3, -4]", BuiltInType.Int16, isArray: true))!);
            CollectionAssert.AreEqual(new[] { "a", "b" }, (string[])Boxed(Encode("[\"a\", \"b\"]", BuiltInType.String, isArray: true))!);
            Assert.HasCount(0, (int[])Boxed(Encode("[]", BuiltInType.Int32, isArray: true))!);
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse("[1, \"x\"]", BuiltInType.Double, isArray: true).Code);
            Assert.AreEqual(StatusCodes.BadOutOfRange, Refuse("[1, 70000]", BuiltInType.Int16, isArray: true).Code);
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse("5", BuiltInType.Double, isArray: true).Code);
        }
    }
}
