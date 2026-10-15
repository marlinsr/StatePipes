using Opc.Ua;
using StatePipes.Common;
using StatePipes.Messages;
using StatePipes.OpcUaBridge;
using StatePipes.OpcUaBridge.Messages;
using StatePipes.SelfDescription;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class OpcUaMonitorMessagesTest
    {
        private const string Exchange = "Line1Plc";

        [TestMethod]
        public void ToMessageNames_MonitorCommand_SharesGetBaseName()
        {
            var names = OpcUaNaming.ToMessageNames(Exchange, new NodeId("Machine.Motor.Speed", 2));
            Assert.AreEqual("Line1Plc.Monitor_ns2_s_Machine_Motor_Speed_Command", names.MonitorCommandTypeFullName);
        }

        [TestMethod]
        public void ToMessageNames_VeryLongIdentifier_MonitorNameFitsRoutingKey()
        {
            var path = string.Join('.', Enumerable.Repeat("Segment", 40));
            var names = OpcUaNaming.ToMessageNames(Exchange, new NodeId(path + ".Value1", 2));
            Assert.IsLessThanOrEqualTo(255, $"{names.MonitorCommandTypeFullName}_999".Length);
        }

        [TestMethod]
        public void ToMessageNames_LongExchangeAndIdentifier_MonitorNameFitsRoutingKey()
        {
            var exchange = string.Join('.', Enumerable.Repeat("Exchange", 30));
            var path = string.Join('.', Enumerable.Repeat("Segment", 40));
            var names = OpcUaNaming.ToMessageNames(exchange, new NodeId(path, 2));
            Assert.IsLessThanOrEqualTo(255, $"{names.MonitorCommandTypeFullName}_999".Length);
        }

        [TestMethod]
        public void AssignUniqueNames_Collision_MonitorNameGetsSameSuffix()
        {
            var dashed = new NodeId("A-B", 2);
            var underscored = new NodeId("A_B", 2);
            var names = OpcUaNaming.AssignUniqueNames(Exchange, [underscored, dashed]);
            Assert.AreEqual("Line1Plc.Monitor_ns2_s_A_B_Command", names[dashed].MonitorCommandTypeFullName);
            Assert.AreEqual("Line1Plc.Monitor_ns2_s_A_B_2_Command", names[underscored].MonitorCommandTypeFullName);
        }

        [TestMethod]
        public void SelfDescription_ConsumedLikeExplorer_MonitorAndFixedTypesEmit()
        {
            NodeId nodeId = new("Line1.Temperature", 2);
            OpcUaDataItem temperature = new(nodeId, nodeId.ToString(), OpcUaNaming.ToMessageNames(Exchange, nodeId), typeof(double));
            var json = JsonUtility.GetJsonStringForObject(new SelfDescriptionEvent(SelfDescriptionBuilder.Build([temperature])), true);
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

            var monitor = emitted[temperature.MonitorCommandTypeFullName];
            Assert.IsTrue(typeof(StatePipes.Interfaces.ICommand).IsAssignableFrom(monitor));
            Assert.AreEqual(typeof(string), monitor.GetProperty("MonitorName")!.PropertyType);
            CollectionAssert.AreEqual(new[] { "Disabled", "Reporting" }, Enum.GetNames(monitor.GetProperty("MonitoringMode")!.PropertyType));
            CollectionAssert.AreEqual(new[] { "Status", "StatusValue", "StatusValueTimestamp" }, Enum.GetNames(monitor.GetProperty("DataChangeTrigger")!.PropertyType));
            CollectionAssert.AreEqual(new[] { "None", "Absolute", "Percent" }, Enum.GetNames(monitor.GetProperty("DeadbandType")!.PropertyType));
            Assert.AreEqual(typeof(double), monitor.GetProperty("DeadbandValue")!.PropertyType);
            Assert.AreEqual(typeof(double), monitor.GetProperty("SamplingInterval")!.PropertyType);
            Assert.AreEqual(typeof(uint), monitor.GetProperty("QueueSize")!.PropertyType);
            Assert.AreEqual(typeof(double), monitor.GetProperty("PublishingInterval")!.PropertyType);

            Assert.IsTrue(typeof(StatePipes.Interfaces.ICommand).IsAssignableFrom(emitted[typeof(DeleteMonitorCommand).FullName!]));
            var statusType = emitted[typeof(MonitorStatusEvent).FullName!];
            Assert.IsTrue(typeof(StatePipes.Interfaces.IEvent).IsAssignableFrom(statusType));

            var statusJson = JsonUtility.GetJsonStringForObject(new MonitorStatusEvent("m1", "ns=2;s=x", MonitorState.Active, 0, "Good", 100, 1, 1000), true);
            dynamic status = JsonUtility.GetObjectFromJson(statusJson, statusType)!;
            Assert.AreEqual("Active", status.State.ToString());
            Assert.AreEqual(1000.0, (double)status.RevisedPublishingInterval);
        }
    }
}
