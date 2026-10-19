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
        private const string Exchange = "Line1Plc";

        [TestMethod]
        public void IsServerOwnNamespace_NamespaceZeroAndDiagnostics_AreServerOwned()
        {
            NamespaceTable namespaceUris = new(["http://opcfoundation.org/UA/", "urn:host:Plc", "http://plcsimulator.org/", "http://opcfoundation.org/UA/Diagnostics"]);
            Assert.IsTrue(OpcUaClient.IsServerOwnNamespace(0, namespaceUris));
            Assert.IsTrue(OpcUaClient.IsServerOwnNamespace(3, namespaceUris));
            Assert.IsFalse(OpcUaClient.IsServerOwnNamespace(1, namespaceUris));
            Assert.IsFalse(OpcUaClient.IsServerOwnNamespace(2, namespaceUris));
            Assert.IsFalse(OpcUaClient.IsServerOwnNamespace(9, namespaceUris));
        }

        [TestMethod]
        public void ToMessageNames_DottedStringNodeId_NamespaceIsExchangeName()
        {
            var names = OpcUaNaming.ToMessageNames(Exchange, new NodeId("Machine.Motor.Speed", 2));
            Assert.AreEqual("Line1Plc", names.Namespace);
            Assert.AreEqual("Line1Plc.Get_ns2_s_Machine_Motor_Speed_Command", names.CommandTypeFullName);
            Assert.AreEqual("Line1Plc.Get_ns2_s_Machine_Motor_Speed_Event", names.EventTypeFullName);
        }

        [TestMethod]
        public void ToMessageNames_UndottedOrNonStringNodeId_NamespaceIsExchangeName()
        {
            Assert.AreEqual("Line1Plc.Get_ns2_s_Speed_Command", OpcUaNaming.ToMessageNames(Exchange, new NodeId("Speed", 2)).CommandTypeFullName);
            Assert.AreEqual("Line1Plc.Get_ns3_i_1001_Command", OpcUaNaming.ToMessageNames(Exchange, new NodeId(1001u, 3)).CommandTypeFullName);
            Assert.AreEqual("Line1Plc.Get_ns3_s_1001_Command", OpcUaNaming.ToMessageNames(Exchange, new NodeId("1001", 3)).CommandTypeFullName);
        }

        [TestMethod]
        public void ToMessageNames_SetNames_ShareBaseAndSuffix()
        {
            var names = OpcUaNaming.ToMessageNames(Exchange, new NodeId("Tank.Level", 2));
            Assert.AreEqual("Line1Plc.Set_ns2_s_Tank_Level_Command", names.SetCommandTypeFullName);
            Assert.AreEqual("Line1Plc.SetStatus_ns2_s_Tank_Level_Event", names.SetStatusEventTypeFullName);
            var suffixed = names with { Name = "Get_ns2_s_X_2" };
            Assert.AreEqual("Line1Plc.Set_ns2_s_X_2_Command", suffixed.SetCommandTypeFullName);
            Assert.AreEqual("Line1Plc.SetStatus_ns2_s_X_2_Event", suffixed.SetStatusEventTypeFullName);
        }

        [TestMethod]
        public void ToMessageNames_VeryLongIdentifier_SetStatusFitsRoutingKey()
        {
            var path = string.Join('.', Enumerable.Repeat("Segment", 40));
            var names = OpcUaNaming.ToMessageNames(Exchange, new NodeId(path, 2));
            Assert.IsLessThanOrEqualTo(255, (names with { Name = names.Name + "_999" }).SetStatusEventTypeFullName.Length);
        }

        [TestMethod]
        public void FromSetCommandFullName_RecognisesSetOnly()
        {
            var names = OpcUaNaming.ToMessageNames(Exchange, new NodeId("Tank.Level", 2));
            var parsed = MessageNames.FromSetCommandFullName(names.SetCommandTypeFullName, Exchange);
            Assert.AreEqual(names, parsed);
            Assert.IsNull(MessageNames.FromSetCommandFullName(names.MonitorCommandTypeFullName, Exchange));
            Assert.IsNull(MessageNames.FromSetCommandFullName(names.DeleteCommandTypeFullName, Exchange));
            Assert.IsNull(MessageNames.FromSetCommandFullName(names.CommandTypeFullName, Exchange));
            Assert.IsNull(MessageNames.FromSetCommandFullName("Other.Set_ns2_s_X_Command", Exchange));
            Assert.IsNull(MessageNames.FromCommandFullName(names.SetCommandTypeFullName, Exchange));
        }

        private static NodeId Ns2(string id) => new(id, 2);

        [TestMethod]
        public void ExecNames_ForMethod()
        {
            var names = OpcUaNaming.AssignUniqueMethodNames(Exchange, [(Ns2("PLC_Data"), Ns2("CalculatePressureVolumeMass"))]).Values.Single();
            Assert.AreEqual("Line1Plc.Exec_ns2_s_CalculatePressureVolumeMass_Command", names.ExecCommandTypeFullName);
            Assert.AreEqual("Line1Plc.ExecResult_ns2_s_CalculatePressureVolumeMass_Event", names.ExecResultEventTypeFullName);
        }

        [TestMethod]
        public void AssignUniqueMethodNames_SharedMethod_EveryPairQualified()
        {
            var names = OpcUaNaming.AssignUniqueMethodNames(Exchange,
                [(Ns2("Pump1"), Ns2("Start")), (Ns2("Pump2"), Ns2("Start")), (Ns2("Obj"), Ns2("Add"))]);
            Assert.AreEqual("Line1Plc.Exec_ns2_s_Pump1__ns2_s_Start_Command", names[(Ns2("Pump1"), Ns2("Start"))].ExecCommandTypeFullName);
            Assert.AreEqual("Line1Plc.Exec_ns2_s_Pump2__ns2_s_Start_Command", names[(Ns2("Pump2"), Ns2("Start"))].ExecCommandTypeFullName);
            Assert.AreEqual("Line1Plc.Exec_ns2_s_Add_Command", names[(Ns2("Obj"), Ns2("Add"))].ExecCommandTypeFullName);
        }

        [TestMethod]
        public void AssignUniqueMethodNames_SanitizeCollision_Suffixed()
        {
            List<string> collisions = [];
            var names = OpcUaNaming.AssignUniqueMethodNames(Exchange, [(Ns2("Obj"), Ns2("A-B")), (Ns2("Obj"), Ns2("A_B"))], collisions.Add);
            CollectionAssert.AreEquivalent(new[] { "Line1Plc.Exec_ns2_s_A_B_Command", "Line1Plc.Exec_ns2_s_A_B_2_Command" },
                names.Values.Select(n => n.ExecCommandTypeFullName).ToArray());
            Assert.HasCount(1, collisions);
        }

        [TestMethod]
        public void AssignUniqueMethodNames_VeryLong_FitsRoutingKey()
        {
            var longObject = Ns2(new string('o', 400));
            var longMethod = Ns2(new string('m', 400));
            var names = OpcUaNaming.AssignUniqueMethodNames(Exchange, [(longObject, longMethod), (Ns2("Other"), longMethod)]);
            foreach (var n in names.Values)
            {
                Assert.IsLessThanOrEqualTo(255, n.ExecCommandTypeFullName.Length);
                Assert.IsLessThanOrEqualTo(255, n.ExecResultEventTypeFullName.Length);
            }
            Assert.AreEqual(2, names.Values.Select(n => n.ExecCommandTypeFullName).Distinct().Count());
        }

        [TestMethod]
        public void FromExecCommandFullName_RecognisesExecOnly()
        {
            var names = OpcUaNaming.AssignUniqueMethodNames(Exchange, [(Ns2("Obj"), Ns2("Add"))]).Values.Single();
            Assert.AreEqual(names, MessageNames.FromExecCommandFullName(names.ExecCommandTypeFullName, Exchange));
            var item = OpcUaNaming.ToMessageNames(Exchange, Ns2("Add"));
            Assert.IsNull(MessageNames.FromExecCommandFullName(item.SetCommandTypeFullName, Exchange));
            Assert.IsNull(MessageNames.FromExecCommandFullName(item.MonitorCommandTypeFullName, Exchange));
            Assert.IsNull(MessageNames.FromExecCommandFullName(item.CommandTypeFullName, Exchange));
            Assert.IsNull(MessageNames.FromSetCommandFullName(names.ExecCommandTypeFullName, Exchange));
            Assert.IsNull(MessageNames.FromCommandFullName(names.ExecCommandTypeFullName, Exchange));
        }

        [TestMethod]
        public void AssignUniqueNames_SameIdentifierDifferentIdType_NoCollision()
        {
            List<string> collisions = [];
            var numeric = new NodeId(5u, 2);
            var text = new NodeId("5", 2);
            var names = OpcUaNaming.AssignUniqueNames(Exchange, [text, numeric], collisions.Add);
            Assert.AreEqual("Line1Plc.Get_ns2_i_5_Command", names[numeric].CommandTypeFullName);
            Assert.AreEqual("Line1Plc.Get_ns2_s_5_Command", names[text].CommandTypeFullName);
            Assert.AreEqual(0, collisions.Count);
        }

        [TestMethod]
        public void ToMessageNames_CharactersInvalidInTypeNames_BecomeUnderscores()
        {
            var names = OpcUaNaming.ToMessageNames(Exchange, new NodeId("Line 1..[Temp-C]", 2));
            Assert.AreEqual("Line1Plc", names.Namespace);
            Assert.AreEqual("Get_ns2_s_Line_1___Temp_C_", names.Name);
        }

        [TestMethod]
        public void ToMessageNames_VeryLongIdentifier_FitsRoutingKeyAndStaysDistinct()
        {
            var path = string.Join('.', Enumerable.Repeat("Segment", 40));
            var first = OpcUaNaming.ToMessageNames(Exchange, new NodeId(path + ".Value1", 2));
            var second = OpcUaNaming.ToMessageNames(Exchange, new NodeId(path + ".Value2", 2));
            Assert.IsLessThanOrEqualTo(OpcUaNaming.MaxNamespaceLength, first.Namespace.Length);
            Assert.IsLessThanOrEqualTo(255, $"{first.CommandTypeFullName}_999".Length);
            Assert.AreNotEqual(first.Name, second.Name);
        }

        [TestMethod]
        public void ToMessageNames_SameLeafInDifferentFolders_NamesStayUnique()
        {
            var first = OpcUaNaming.ToMessageNames(Exchange, new NodeId("Motor1.Speed", 2));
            var second = OpcUaNaming.ToMessageNames(Exchange, new NodeId("Motor2.Speed", 2));
            Assert.AreNotEqual(first.Name, second.Name);
        }

        [TestMethod]
        public void AssignUniqueNames_SanitizedCollision_GetsDeterministicSuffix()
        {
            var dashed = new NodeId("A-B", 2);
            var underscored = new NodeId("A_B", 2);
            List<string> collisions = [];

            var names = OpcUaNaming.AssignUniqueNames(Exchange, [underscored, dashed], collisions.Add);

            // "ns=2;s=A-B" sorts before "ns=2;s=A_B", so the dashed node keeps the plain name whatever the input order.
            Assert.AreEqual("Line1Plc.Get_ns2_s_A_B_Command", names[dashed].CommandTypeFullName);
            Assert.AreEqual("Line1Plc.Get_ns2_s_A_B_2_Command", names[underscored].CommandTypeFullName);
            Assert.HasCount(1, collisions);
        }

        [TestMethod]
        public void AssignUniqueNames_CollisionSuffix_NeverTakesAnotherNodesOwnName()
        {
            var dashed = new NodeId("A-B", 2);
            var underscored = new NodeId("A_B", 2);
            var realSuffix = new NodeId("A_B_2", 2);

            var names = OpcUaNaming.AssignUniqueNames(Exchange, [realSuffix, underscored, dashed]);

            Assert.AreEqual("Get_ns2_s_A_B", names[dashed].Name);
            Assert.AreEqual("Get_ns2_s_A_B_2", names[realSuffix].Name);
            Assert.AreEqual("Get_ns2_s_A_B_3", names[underscored].Name);
        }

        [TestMethod]
        public void AssignUniqueNames_DotAndUnderscore_Collide()
        {
            // Dots and underscores both become underscores, so both nodes sanitize to Get_ns2_s_A_B_C.
            var dotFirst = new NodeId("A.B_C", 2);
            var dotSecond = new NodeId("A_B.C", 2);
            List<string> collisions = [];

            var names = OpcUaNaming.AssignUniqueNames(Exchange, [dotSecond, dotFirst], collisions.Add);

            Assert.AreEqual("Line1Plc.Get_ns2_s_A_B_C_Command", names[dotFirst].CommandTypeFullName);
            Assert.AreEqual("Line1Plc.Get_ns2_s_A_B_C_2_Command", names[dotSecond].CommandTypeFullName);
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

        [TestMethod]
        public void SelfDescription_SetMessagesOnlyForWritableItems()
        {
            var writable = CreateItem("Line1.Setpoint", typeof(double)) with { BuiltInType = BuiltInType.Double, IsWritable = true };
            var readOnly = CreateItem("Line1.Temperature", typeof(double)) with { BuiltInType = BuiltInType.Double };

            var names = SelfDescriptionBuilder.Build([writable, readOnly]).TypeSerializations.Select(t => t.FullName).ToList();

            CollectionAssert.Contains(names, writable.SetCommandTypeFullName);
            CollectionAssert.Contains(names, writable.SetStatusEventTypeFullName);
            Assert.IsFalse(names.Any(n => n.Contains("Set_ns2_s_Line1_Temperature") || n.Contains("SetStatus_ns2_s_Line1_Temperature")));
            var setCommand = SelfDescriptionBuilder.Build([writable]).TypeSerializations.Single(t => t.FullName == writable.SetCommandTypeFullName).GetTopLevelDescription();
            Assert.IsTrue(setCommand.IsCommand);
            var value = setCommand.Properties.Single();
            Assert.AreEqual("Value", value.Name);
            Assert.IsFalse(value.IsNullable);
        }

        [TestMethod]
        public void SelfDescription_SizePerWritableItem()
        {
            static int Size(IEnumerable<OpcUaDataItem> items) => JsonUtility.GetJsonStringForObject(new SelfDescriptionEvent(SelfDescriptionBuilder.Build(items)), true).Length;
            var readOnly = Enumerable.Range(0, 100).Select(i => CreateItem($"Line1.Value{i}", typeof(double)) with { BuiltInType = BuiltInType.Double }).ToList();
            var writable = readOnly.Select(item => item with { IsWritable = true }).ToList();

            var perItem = (Size(writable) - Size(readOnly)) / 100;

            Console.WriteLine($"A writable Double item adds {perItem} bytes to the self description");
            Assert.IsLessThan(6 * 1024, perItem);
        }

        /// <summary>
        /// The self description must be usable the way StatePipes.Explorer uses it: round-trip it through JSON,
        /// emit real types from it, build example commands, and deserialize a Get event body into the emitted type.
        /// </summary>
        [TestMethod]
        public void SelfDescription_ConsumedLikeExplorer_EmitsTypesThatGetEventsDeserializeInto()
        {
            OpcUaDataItem temperature = CreateItem("Line1.Temperature", typeof(double));
            OpcUaDataItem samples = CreateItem("Line1.Samples", typeof(int[]));
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
            Assert.AreEqual("Line1Plc.Get_ns2_s_Line1_Temperature_Command", emitted[temperature.CommandTypeFullName].FullName);
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
            body = OpcUaBridgeService.CreateGetEventBody(temperature, DataValue.FromStatusCode(StatusCodes.BadNotConnected));
            Assert.DoesNotContain("\"Value\"", Encoding.UTF8.GetString(body));
            getEvent = JsonUtility.GetObjectFromJson(Encoding.UTF8.GetString(body), emitted[temperature.EventTypeFullName])!;
            Assert.AreEqual((uint)StatusCodes.BadNotConnected, (uint)getEvent.StatusCode);
        }

        private static OpcUaDataItem CreateItem(string identifier, Type valueType)
        {
            NodeId nodeId = new(identifier, 2);
            return new(nodeId, nodeId.ToString(), OpcUaNaming.ToMessageNames(Exchange, nodeId), valueType);
        }
    }
}
