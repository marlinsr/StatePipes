using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.Common;
using StatePipes.Messages;
using StatePipes.OpcUaBridge;
using StatePipes.SelfDescription;
using System.Text;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>
    /// Structured OPC UA values: the layout read from a DataType's StructureDefinition, the binary decoding that
    /// follows it, and the self description that lets clients emit a real type for it.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class OpcUaStructureTest
    {
        internal const string Exchange = "Line1Plc";
        internal static readonly NodeId DensityTypeId = new("DensityDataType", 2);
        internal static readonly NodeId TankTypeId = new("TankDataType", 2);
        internal static readonly NodeId TreeTypeId = new("TreeDataType", 2);

        internal static readonly StructureDefinition DensityDefinition = Encoded("DensityDataType",
            Field("Volume", DataTypeIds.Double),
            Field("Mass", DataTypeIds.Double));

        internal static readonly StructureDefinition TankDefinition = Encoded("TankDataType",
            Field("Name", DataTypeIds.String),
            Field("Density", DensityTypeId),
            Field("History", DensityTypeId, ValueRanks.OneDimension),
            Field("Readings", DataTypeIds.Double, ValueRanks.OneDimension));

        internal static readonly StructureDefinition TreeDefinition = Encoded("TreeDataType",
            Field("Label", DataTypeIds.String),
            Field("Children", TreeTypeId, ValueRanks.OneDimension));

        [TestMethod]
        public async Task BuildLayout_NestedStructuresAndArrays_FollowsTheDefinition()
        {
            var tank = await CreateBuilder().BuildAsync(TankTypeId, CancellationToken.None);

            Assert.IsNotNull(tank);
            Assert.AreEqual("Line1Plc.Types.ns2_s_TankDataType", tank.FullName);
            CollectionAssert.AreEqual(new[] { "Name", "Density", "History", "Readings" }, tank.Fields.Select(f => f.Name).ToArray());
            Assert.AreEqual(typeof(string), tank.Fields[0].ClrType);
            var density = tank.Fields[1].Structure;
            Assert.IsNotNull(density);
            Assert.AreEqual("Line1Plc.Types.ns2_s_DensityDataType", density.FullName);
            Assert.IsFalse(tank.Fields[1].IsArray);
            Assert.AreSame(density, tank.Fields[2].Structure, "A type used twice has one layout");
            Assert.IsTrue(tank.Fields[2].IsArray);
            Assert.AreEqual(typeof(double), tank.Fields[3].ClrType);
            Assert.IsTrue(tank.Fields[3].IsArray);
        }

        [TestMethod]
        public async Task BuildLayout_RecordsDefaultEncodingId()
        {
            var plain = new NodeId("Plain", 2);
            var builder = CreateBuilder(new() { [plain] = Structure(Definition(Field("A", DataTypeIds.Double))) });

            var density = await builder.BuildAsync(DensityTypeId, CancellationToken.None);
            var withoutEncoding = await builder.BuildAsync(plain, CancellationToken.None);

            Assert.AreEqual(new NodeId("DensityDataType_Encoding_DefaultBinary", 2), density!.BinaryEncodingId);
            Assert.IsNull(withoutEncoding!.BinaryEncodingId);
        }

        [TestMethod]
        public async Task BuildLayout_RecursiveType_PointsBackAtItself()
        {
            var tree = await CreateBuilder().BuildAsync(TreeTypeId, CancellationToken.None);

            Assert.IsNotNull(tree);
            Assert.AreSame(tree, tree.Fields[1].Structure);
        }

        [TestMethod]
        public async Task BuildLayout_UndecodableStructure_HasNoLayout()
        {
            var union = new NodeId("Union", 2);
            var optional = new NodeId("Optional", 2);
            var undescribed = new NodeId("Undescribed", 2);
            var matrix = new NodeId("Matrix", 2);
            var containsUnion = new NodeId("ContainsUnion", 2);
            var unionDefinition = Definition(Field("A", DataTypeIds.Double));
            unionDefinition.StructureType = StructureType.Union;
            var optionalDefinition = Definition(Field("A", DataTypeIds.Double));
            optionalDefinition.StructureType = StructureType.StructureWithOptionalFields;
            var builder = CreateBuilder(new()
            {
                [union] = Structure(unionDefinition),
                [optional] = Structure(optionalDefinition),
                [undescribed] = new DataTypeInfo(BuiltInType.ExtensionObject, false, null),
                [matrix] = Structure(Definition(Field("Grid", DataTypeIds.Double, ValueRanks.TwoDimensions))),
                [containsUnion] = Structure(Definition(Field("Inner", union)))
            });

            Assert.IsNull(await builder.BuildAsync(union, CancellationToken.None));
            Assert.IsNull(await builder.BuildAsync(optional, CancellationToken.None));
            Assert.IsNull(await builder.BuildAsync(undescribed, CancellationToken.None));
            Assert.IsNull(await builder.BuildAsync(matrix, CancellationToken.None));
            Assert.IsNull(await builder.BuildAsync(containsUnion, CancellationToken.None));
        }

        [TestMethod]
        public async Task BuildLayout_AbstractFields_TravelAsTheirFallbackTypes()
        {
            var holder = new NodeId("Holder", 2);
            var builder = CreateBuilder(new()
            {
                [holder] = Structure(Definition(Field("Anything", DataTypeIds.Structure), Field("Number", DataTypeIds.Number), Field("Variant", DataTypeIds.BaseDataType)))
            });

            var layout = await builder.BuildAsync(holder, CancellationToken.None);

            Assert.IsNotNull(layout);
            Assert.AreEqual(BuiltInType.ExtensionObject, layout.Fields[0].BuiltInType);
            Assert.AreEqual(typeof(string), layout.Fields[0].ClrType);
            Assert.AreEqual(typeof(double), layout.Fields[1].ClrType);
            Assert.AreEqual(BuiltInType.Variant, layout.Fields[2].BuiltInType);
            Assert.AreEqual(typeof(string), layout.Fields[2].ClrType);
        }

        [TestMethod]
        public async Task Decode_NestedStructureWithArrays_ReadsEveryField()
        {
            var tank = (await CreateBuilder().BuildAsync(TankTypeId, CancellationToken.None))!;

            var value = (JObject)StructureDecoder.ToJToken(new ExtensionObject(new ExpandedNodeId(TankTypeId), EncodeTank().ToByteString()), tank, isArray: false);

            Assert.AreEqual("T1", value["Name"]!.Value<string>());
            Assert.AreEqual(1.0, value["Density"]!["Volume"]!.Value<double>());
            Assert.AreEqual(2.0, value["Density"]!["Mass"]!.Value<double>());
            Assert.AreEqual(2, ((JArray)value["History"]!).Count);
            Assert.AreEqual(6.0, value["History"]![1]!["Mass"]!.Value<double>());
            CollectionAssert.AreEqual(new[] { 1.5, 2.5 }, value["Readings"]!.ToObject<double[]>());
        }

        [TestMethod]
        public async Task Decode_BodyThatDoesNotMatchTheLayout_IsNull()
        {
            var tank = (await CreateBuilder().BuildAsync(TankTypeId, CancellationToken.None))!;
            var bytes = EncodeTank();

            Assert.AreEqual(JTokenType.Null, StructureDecoder.ToJToken(new ExtensionObject(new ExpandedNodeId(TankTypeId), bytes[..^3].ToByteString()), tank, false).Type);
            Assert.AreEqual(JTokenType.Null, StructureDecoder.ToJToken(new ExtensionObject(new ExpandedNodeId(TankTypeId), bytes.Concat(new byte[] { 1 }).ToArray().ToByteString()), tank, false).Type);
            Assert.AreEqual(JTokenType.Null, StructureDecoder.ToJToken("not a structure", tank, false).Type);
            Assert.AreEqual(JTokenType.Null, StructureDecoder.ToJToken(null, tank, false).Type);
        }

        /// <summary>
        /// The Density bug end to end: a structured variable must reach the client as a type it can emit, with
        /// the fields readable after deserializing the Get event, not as an opaque string.
        /// </summary>
        [TestMethod]
        public async Task SelfDescription_StructuredValues_EmitTypesThatGetEventsDeserializeInto()
        {
            var builder = CreateBuilder();
            var densityLayout = (await builder.BuildAsync(DensityTypeId, CancellationToken.None))!;
            var tankLayout = (await builder.BuildAsync(TankTypeId, CancellationToken.None))!;
            var density = CreateItem("Density", densityLayout, isArray: false);
            var tank = CreateItem("Tank", tankLayout, isArray: false);
            var densities = CreateItem("Densities", densityLayout, isArray: true);
            var emitted = EmitLikeExplorer([density, tank, densities]);
            var now = DateTime.UtcNow;

            var body = OpcUaBridgeService.CreateGetEventBody(density, new DataValue(new Variant(new ExtensionObject(new ExpandedNodeId(DensityTypeId), EncodeDensity(1.0, 2.0).ToByteString())), StatusCodes.Good, now, now));
            dynamic getEvent = Deserialize(body, emitted[density.EventTypeFullName]);
            Assert.AreEqual("Line1Plc.Types.ns2_s_DensityDataType", ((object)getEvent.Value).GetType().FullName);
            Assert.AreEqual(1.0, (double)getEvent.Value.Volume);
            Assert.AreEqual(2.0, (double)getEvent.Value.Mass);

            body = OpcUaBridgeService.CreateGetEventBody(tank, new DataValue(new Variant(new ExtensionObject(new ExpandedNodeId(TankTypeId), EncodeTank().ToByteString())), StatusCodes.Good, now, now));
            getEvent = Deserialize(body, emitted[tank.EventTypeFullName]);
            Assert.AreEqual("T1", (string)getEvent.Value.Name);
            Assert.AreEqual(2.0, (double)getEvent.Value.Density.Mass);
            Assert.AreEqual(6.0, (double)getEvent.Value.History[1].Mass);
            CollectionAssert.AreEqual(new[] { 1.5, 2.5 }, (double[])getEvent.Value.Readings);

            ExtensionObject[] array = [new(new ExpandedNodeId(DensityTypeId), EncodeDensity(5.0, 6.0).ToByteString()), new(new ExpandedNodeId(DensityTypeId), EncodeDensity(7.0, 8.0).ToByteString())];
            body = OpcUaBridgeService.CreateGetEventBody(densities, new DataValue(new Variant(array), StatusCodes.Good, now, now));
            getEvent = Deserialize(body, emitted[densities.EventTypeFullName]);
            Assert.AreEqual(8.0, (double)getEvent.Value[1].Mass);

            // A body the layout cannot decode leaves Value out, like a failed read, so the event still deserializes.
            body = OpcUaBridgeService.CreateGetEventBody(density, new DataValue(new Variant(new ExtensionObject(new ExpandedNodeId(DensityTypeId), new byte[] { 1, 2 }.ToByteString())), StatusCodes.Good, now, now));
            Assert.DoesNotContain("\"Value\"", Encoding.UTF8.GetString(body));
            Deserialize(body, emitted[density.EventTypeFullName]);
        }

        private static Dictionary<string, Type> EmitLikeExplorer(IEnumerable<OpcUaDataItem> items)
        {
            var json = JsonUtility.GetJsonStringForObject(new SelfDescriptionEvent(SelfDescriptionBuilder.Build(items)), true);
            var received = JsonUtility.GetObjectForJsonString<SelfDescriptionEvent>(json)!;
            TypeSerializationConverter converter = new();
            Dictionary<string, Type> emitted = [];
            foreach (var typeSerialization in received.TypeList.TypeSerializations)
            {
                TypeSerializationJsonHelper helper = new(typeSerialization, converter);
                Assert.IsNotNull(helper.ThisType, typeSerialization.FullName);
                Assert.IsNotNull(helper.GenerateExampleJson(), typeSerialization.FullName);
                emitted[typeSerialization.FullName] = helper.ThisType;
            }
            return emitted;
        }

        private static dynamic Deserialize(byte[] body, Type type) => JsonUtility.GetObjectFromJson(Encoding.UTF8.GetString(body), type)!;

        private static OpcUaDataItem CreateItem(string identifier, StructureLayout layout, bool isArray)
        {
            NodeId nodeId = new(identifier, 2);
            return new(nodeId, nodeId.ToString(), OpcUaNaming.ToMessageNames(Exchange, nodeId), typeof(string), layout, isArray);
        }

        private static byte[] EncodeDensity(double volume, double mass) => Encode(encoder => WriteDensity(encoder, volume, mass));

        private static byte[] EncodeTank() => Encode(encoder =>
        {
            encoder.WriteString("Name", "T1");
            WriteDensity(encoder, 1.0, 2.0);
            encoder.WriteInt32(null, 2);
            WriteDensity(encoder, 3.0, 4.0);
            WriteDensity(encoder, 5.0, 6.0);
            encoder.WriteDoubleArray("Readings", [1.5, 2.5]);
        });

        private static void WriteDensity(BinaryEncoder encoder, double volume, double mass)
        {
            encoder.WriteDouble("Volume", volume);
            encoder.WriteDouble("Mass", mass);
        }

        private static byte[] Encode(Action<BinaryEncoder> write)
        {
            using BinaryEncoder encoder = new(ServiceMessageContext.Create(DefaultTelemetry.Create(_ => { })));
            write(encoder);
            return encoder.CloseAndReturnBuffer() ?? [];
        }

        internal static StructureLayoutBuilder CreateBuilder(Dictionary<NodeId, DataTypeInfo>? extra = null)
        {
            Dictionary<NodeId, DataTypeInfo> types = new()
            {
                [DataTypeIds.Double] = new(BuiltInType.Double, false, null),
                [DataTypeIds.String] = new(BuiltInType.String, false, null),
                [DataTypeIds.Number] = new(BuiltInType.Number, true, null),
                [DataTypeIds.BaseDataType] = new(BuiltInType.Variant, true, null),
                [DataTypeIds.Structure] = new(BuiltInType.ExtensionObject, true, null),
                [DensityTypeId] = Structure(DensityDefinition),
                [TankTypeId] = Structure(TankDefinition),
                [TreeTypeId] = Structure(TreeDefinition)
            };
            foreach (var (id, info) in extra ?? []) types[id] = info;
            return new StructureLayoutBuilder(Exchange, (id, _) => Task.FromResult(types.GetValueOrDefault(id)));
        }

        internal static DataTypeInfo Structure(StructureDefinition definition) => new(BuiltInType.ExtensionObject, false, definition);

        internal static StructureDefinition Definition(params StructureField[] fields) => new()
        {
            BaseDataType = DataTypeIds.Structure,
            StructureType = StructureType.Structure,
            Fields = [.. fields]
        };

        /// <summary>A definition with a default binary encoding, named as OpcUaServer names its encodings.</summary>
        internal static StructureDefinition Encoded(string typeName, params StructureField[] fields)
        {
            var definition = Definition(fields);
            definition.DefaultEncodingId = new NodeId($"{typeName}_Encoding_DefaultBinary", 2);
            return definition;
        }

        internal static StructureField Field(string name, NodeId dataType, int valueRank = ValueRanks.Scalar) =>
            new() { Name = name, DataType = dataType, ValueRank = valueRank };
    }
}
