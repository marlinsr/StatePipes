using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.Common;
using StatePipes.Messages;
using StatePipes.OpcUaBridge;
using StatePipes.SelfDescription;
using System.Text;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class OpcUaBridgeTest
    {
        [TestMethod]
        public void ToMessageName_StringNodeId_UsesNsAndSWithDotsReplaced()
        {
            Assert.AreEqual("Get_ns2_s_Line1_Temperature", OpcUaNaming.ToMessageName(new NodeId("Line1.Temperature", 2)));
        }

        [TestMethod]
        public void ToMessageName_OtherIdentifierTypes_KeepTheirLetterSoTheyCannotCollideWithStrings()
        {
            Assert.AreEqual("Get_ns3_i_1001", OpcUaNaming.ToMessageName(new NodeId(1001u, 3)));
            Assert.AreEqual("Get_ns3_s_1001", OpcUaNaming.ToMessageName(new NodeId("1001", 3)));
        }

        [TestMethod]
        public void ToMessageName_CharactersInvalidInTypeNames_BecomeUnderscores()
        {
            Assert.AreEqual("Get_ns2_s_Line_1__Temp_C_", OpcUaNaming.ToMessageName(new NodeId("Line 1/[Temp-C]", 2)));
        }

        [TestMethod]
        public void ToMessageName_VeryLongIdentifier_FitsRoutingKeyAndStaysDistinct()
        {
            var prefix = new string('a', 400);
            var first = OpcUaNaming.ToMessageName(new NodeId(prefix + "1", 2));
            var second = OpcUaNaming.ToMessageName(new NodeId(prefix + "2", 2));
            Assert.IsLessThanOrEqualTo(OpcUaNaming.MaxMessageNameLength, first.Length);
            Assert.IsLessThanOrEqualTo(255, $"{OpcUaNaming.CommandNamespace}.{first}_999".Length);
            Assert.AreNotEqual(first, second);
        }

        [TestMethod]
        public void AssignUniqueNames_SanitizedCollision_GetsDeterministicSuffix()
        {
            var dotted = new NodeId("A.B", 2);
            var underscored = new NodeId("A_B", 2);
            List<string> collisions = [];

            var names = OpcUaNaming.AssignUniqueNames([underscored, dotted], collisions.Add);

            // "ns=2;s=A.B" sorts before "ns=2;s=A_B", so the dotted node keeps the plain name whatever the input order.
            Assert.AreEqual("Get_ns2_s_A_B", names[dotted]);
            Assert.AreEqual("Get_ns2_s_A_B_2", names[underscored]);
            Assert.HasCount(1, collisions);
        }

        [TestMethod]
        public void MapToClrType_NaturalTypes_KeepTheirTypeAndArrays()
        {
            Assert.AreEqual(typeof(double), OpcUaValueMapper.MapToClrType(BuiltInType.Double, ValueRanks.Scalar));
            Assert.AreEqual(typeof(int[]), OpcUaValueMapper.MapToClrType(BuiltInType.Int32, ValueRanks.OneDimension));
            Assert.AreEqual(typeof(string[]), OpcUaValueMapper.MapToClrType(BuiltInType.String, ValueRanks.OneDimension));
            Assert.AreEqual(typeof(int), OpcUaValueMapper.MapToClrType(BuiltInType.Enumeration, ValueRanks.Scalar));
        }

        [TestMethod]
        public void MapToClrType_NoNaturalTypeOrUnfixedRank_IsString()
        {
            Assert.AreEqual(typeof(string), OpcUaValueMapper.MapToClrType(BuiltInType.LocalizedText, ValueRanks.Scalar));
            Assert.AreEqual(typeof(string), OpcUaValueMapper.MapToClrType(BuiltInType.ExtensionObject, ValueRanks.OneDimension));
            Assert.AreEqual(typeof(string), OpcUaValueMapper.MapToClrType(BuiltInType.Double, ValueRanks.TwoDimensions));
            Assert.AreEqual(typeof(string), OpcUaValueMapper.MapToClrType(BuiltInType.Double, ValueRanks.Any));
        }

        [TestMethod]
        public void ToJToken_ConvertsToTheAdvertisedType()
        {
            Assert.AreEqual(3.5, OpcUaValueMapper.ToJToken(3.5f, typeof(double)).Value<double>());
            Assert.AreEqual("hello", OpcUaValueMapper.ToJToken(new LocalizedText("en", "hello"), typeof(string)).Value<string>());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, OpcUaValueMapper.ToJToken(new[] { 1, 2, 3 }, typeof(int[])).ToObject<int[]>());
            Assert.AreEqual(JTokenType.Null, OpcUaValueMapper.ToJToken("not a number", typeof(int)).Type);
            Assert.AreEqual(JTokenType.Null, OpcUaValueMapper.ToJToken(null, typeof(double)).Type);
        }

        /// <summary>
        /// The self description must be usable the way StatePipes.Explorer uses it: round-trip it through JSON,
        /// emit real types from it, build example commands, and deserialize a Get event body into the emitted type.
        /// </summary>
        [TestMethod]
        public void SelfDescription_ConsumedLikeExplorer_EmitsTypesThatGetEventsDeserializeInto()
        {
            OpcUaDataItem temperature = new(new NodeId("Line1.Temperature", 2), "ns=2;s=Line1.Temperature", "Get_ns2_s_Line1_Temperature", typeof(double));
            OpcUaDataItem samples = new(new NodeId("Line1.Samples", 2), "ns=2;s=Line1.Samples", "Get_ns2_s_Line1_Samples", typeof(int[]));
            var json = JsonUtility.GetJsonStringForObject(new SelfDescriptionEvent(SelfDescriptionBuilder.Build([temperature, samples])), true);
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
            Assert.IsTrue(typeof(StatePipes.Interfaces.ICommand).IsAssignableFrom(emitted[temperature.CommandTypeFullName]));
            Assert.IsTrue(typeof(StatePipes.Interfaces.IEvent).IsAssignableFrom(emitted[temperature.EventTypeFullName]));

            var now = DateTime.UtcNow;
            var body = OpcUaBridgeService.CreateGetEventBody(temperature, new DataValue(new Variant(21.5), StatusCodes.Good, now, now));
            dynamic getEvent = JsonUtility.GetObjectFromJson(Encoding.UTF8.GetString(body), emitted[temperature.EventTypeFullName])!;
            Assert.AreEqual(21.5, (double?)getEvent.Value);
            Assert.AreEqual("ns=2;s=Line1.Temperature", (string)getEvent.NodeId);
            Assert.AreEqual(0u, (uint)getEvent.StatusCode);

            body = OpcUaBridgeService.CreateGetEventBody(samples, new DataValue(new Variant(new[] { 4, 5 }), StatusCodes.Good, now, now));
            getEvent = JsonUtility.GetObjectFromJson(Encoding.UTF8.GetString(body), emitted[samples.EventTypeFullName])!;
            CollectionAssert.AreEqual(new[] { 4, 5 }, (int[])getEvent.Value);

            // A failed read carries no value at all, so it deserializes even where Value was emitted non-nullable.
            body = OpcUaBridgeService.CreateGetEventBody(temperature, new DataValue(StatusCodes.BadNotConnected));
            Assert.DoesNotContain("\"Value\"", Encoding.UTF8.GetString(body));
            getEvent = JsonUtility.GetObjectFromJson(Encoding.UTF8.GetString(body), emitted[temperature.EventTypeFullName])!;
            Assert.AreEqual((uint)StatusCodes.BadNotConnected, (uint)getEvent.StatusCode);
        }
    }
}
