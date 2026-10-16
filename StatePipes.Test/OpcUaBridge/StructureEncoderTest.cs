using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.OpcUaBridge;
using static StatePipes.Test.OpcUaBridge.OpcUaStructureTest;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>Encoding a Set command's JSON into a structure's binary body, checked by decoding it again.</summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class StructureEncoderTest
    {
        private static readonly NodeId SmallTypeId = new("SmallDataType", 2);
        private static readonly NodeId AddressTypeId = new("AddressDataType", 2);

        private static Dictionary<NodeId, DataTypeInfo> ExtraTypes => new()
        {
            [DataTypeIds.Int16] = new(BuiltInType.Int16, false, null),
            [DataTypeIds.NodeId] = new(BuiltInType.NodeId, false, null),
            [SmallTypeId] = Structure(Encoded("SmallDataType", Field("Count", DataTypeIds.Int16))),
            [AddressTypeId] = Structure(Encoded("AddressDataType", Field("Node", DataTypeIds.NodeId)))
        };

        private static async Task<StructureLayout> LayoutAsync(NodeId dataTypeId, Dictionary<NodeId, DataTypeInfo>? extra = null) =>
            (await CreateBuilder(extra ?? ExtraTypes).BuildAsync(dataTypeId, CancellationToken.None))!;

        private static Variant Encode(string json, StructureLayout layout, bool isArray = false)
        {
            var status = StructureEncoder.TryEncode(JToken.Parse(json), layout, isArray, out var variant);
            Assert.AreEqual(StatusCodes.Good, status.Code, json);
            return variant;
        }

        private static StatusCode Refuse(string? json, StructureLayout layout, bool isArray = false) =>
            StructureEncoder.TryEncode(json == null ? null : JToken.Parse(json), layout, isArray, out _);

        private static void AssertRoundTrips(string json, StructureLayout layout, bool isArray = false)
        {
            var decoded = StructureDecoder.ToJToken(Encode(json, layout, isArray), layout, isArray);
            Assert.IsTrue(JToken.DeepEquals(JToken.Parse(json), decoded), $"Expected {json}, decoded {decoded}");
        }

        private const string Tank = """
            {"Name":"T1","Density":{"Volume":1.5,"Mass":2.0},
             "History":[{"Volume":3.0,"Mass":4.0},{"Volume":5.0,"Mass":6.0}],"Readings":[7.0,8.5]}
            """;

        [TestMethod]
        public async Task Encode_Density_RoundTripsThroughDecoder() =>
            AssertRoundTrips("""{"Volume":1.5,"Mass":2.0}""", await LayoutAsync(DensityTypeId));

        [TestMethod]
        public async Task Encode_TankWithNestedAndArrays_RoundTrips() => AssertRoundTrips(Tank, await LayoutAsync(TankTypeId));

        [TestMethod]
        public async Task Encode_DensityArray_RoundTrips()
        {
            var layout = await LayoutAsync(DensityTypeId);
            const string json = """[{"Volume":1.0,"Mass":2.0},{"Volume":3.0,"Mass":4.0}]""";
            AssertRoundTrips(json, layout, isArray: true);
            var variant = Encode(json, layout, isArray: true);
            Assert.AreEqual(BuiltInType.ExtensionObject, variant.TypeInfo.BuiltInType);
            Assert.AreEqual(ValueRanks.OneDimension, variant.TypeInfo.ValueRank);
        }

        [TestMethod]
        public async Task Encode_TaggedWithBinaryEncodingId()
        {
            var layout = await LayoutAsync(DensityTypeId);
            var extension = (ExtensionObject)Encode("""{"Volume":1.0,"Mass":2.0}""", layout).AsBoxedObject(Variant.BoxingBehavior.Legacy)!;
            Assert.AreEqual((ExpandedNodeId)layout.BinaryEncodingId!.Value, extension.TypeId);
        }

        [TestMethod]
        public async Task Encode_DecodedGetValueWithNullFieldOmitted_RoundTrips() =>
            AssertRoundTrips("""{"Density":{"Volume":1.0,"Mass":2.0},"Readings":[]}""", await LayoutAsync(TankTypeId));

        [TestMethod]
        public async Task Encode_MissingValueTypeField_TypeMismatch()
        {
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse("""{"Volume":1.0}""", await LayoutAsync(DensityTypeId)).Code);
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse("""{"Name":"T1","Readings":[]}""", await LayoutAsync(TankTypeId)).Code);
        }

        [TestMethod]
        public async Task Encode_UnknownField_TypeMismatch() =>
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse("""{"Volume":1.0,"Mass":2.0,"Volum":3.0}""", await LayoutAsync(DensityTypeId)).Code);

        [TestMethod]
        public async Task Encode_FieldOutOfRange_BadOutOfRange()
        {
            var layout = await LayoutAsync(SmallTypeId);
            Assert.AreEqual(StatusCodes.BadOutOfRange, Refuse("""{"Count":70000}""", layout).Code);
            AssertRoundTrips("""{"Count":-7}""", layout);
        }

        [TestMethod]
        public async Task Encode_NotAnObject_TypeMismatch()
        {
            var layout = await LayoutAsync(DensityTypeId);
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse("5", layout).Code);
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse(null, layout).Code);
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse("null", layout).Code);
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse("""{"Volume":1.0,"Mass":2.0}""", layout, isArray: true).Code);
            Assert.AreEqual(StatusCodes.BadTypeMismatch, Refuse("""[{"Volume":1.0,"Mass":2.0}, 5]""", layout, isArray: true).Code);
        }

        [TestMethod]
        public async Task CanEncode_Rules()
        {
            Assert.IsTrue(StructureEncoder.CanEncode(await LayoutAsync(TankTypeId)));
            Assert.IsTrue(StructureEncoder.CanEncode(await LayoutAsync(TreeTypeId)), "A recursive layout terminates");
            Assert.IsFalse(StructureEncoder.CanEncode(await LayoutAsync(AddressTypeId)), "A NodeId field has no natural form");
            var noNestedEncoding = ExtraTypes;
            noNestedEncoding[DensityTypeId] = Structure(Definition(Field("Volume", DataTypeIds.Double), Field("Mass", DataTypeIds.Double)));
            Assert.IsFalse(StructureEncoder.CanEncode(await LayoutAsync(TankTypeId, noNestedEncoding)), "The nested Density has no encoding");
        }
    }
}
