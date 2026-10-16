using Opc.Ua;
using StatePipes.Common;
using StatePipes.Messages;
using StatePipes.OpcUaBridge;
using StatePipes.SelfDescription;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class OpcUaMonitorMessagesTest
    {
        private const string Exchange = "Line1Plc";

        [TestMethod]
        public void ToMessageNames_PerItemNames_ShareGetBase()
        {
            var names = OpcUaNaming.ToMessageNames(Exchange, new NodeId("Machine.Motor.Speed", 2));
            Assert.AreEqual("Line1Plc.Monitor_ns2_s_Machine_Motor_Speed_Command", names.MonitorCommandTypeFullName);
            Assert.AreEqual("Line1Plc.Delete_ns2_s_Machine_Motor_Speed_Command", names.DeleteCommandTypeFullName);
            Assert.AreEqual("Line1Plc.MonitorStatus_ns2_s_Machine_Motor_Speed_Event", names.MonitorStatusEventTypeFullName);
        }

        [TestMethod]
        public void ToMessageNames_VeryLongIdentifier_EveryNameFitsRoutingKey()
        {
            var path = string.Join('.', Enumerable.Repeat("Segment", 40));
            AssertEveryNameFits(OpcUaNaming.ToMessageNames(Exchange, new NodeId(path + ".Value1", 2)));
        }

        [TestMethod]
        public void ToMessageNames_LongExchangeAndIdentifier_EveryNameFitsRoutingKey()
        {
            var exchange = string.Join('.', Enumerable.Repeat("Exchange", 30));
            var path = string.Join('.', Enumerable.Repeat("Segment", 40));
            AssertEveryNameFits(OpcUaNaming.ToMessageNames(exchange, new NodeId(path, 2)));
        }

        private static void AssertEveryNameFits(MessageNames names)
        {
            foreach (var name in new[] { names.CommandTypeFullName, names.EventTypeFullName, names.MonitorCommandTypeFullName, names.DeleteCommandTypeFullName, names.MonitorStatusEventTypeFullName })
                Assert.IsLessThanOrEqualTo(255, $"{name}_999".Length, name);
        }

        [TestMethod]
        public void AssignUniqueNames_Collision_PerItemNamesGetSameSuffix()
        {
            var dashed = new NodeId("A-B", 2);
            var underscored = new NodeId("A_B", 2);
            var names = OpcUaNaming.AssignUniqueNames(Exchange, [underscored, dashed]);
            Assert.AreEqual("Line1Plc.Monitor_ns2_s_A_B_Command", names[dashed].MonitorCommandTypeFullName);
            Assert.AreEqual("Line1Plc.Monitor_ns2_s_A_B_2_Command", names[underscored].MonitorCommandTypeFullName);
            Assert.AreEqual("Line1Plc.Delete_ns2_s_A_B_2_Command", names[underscored].DeleteCommandTypeFullName);
            Assert.AreEqual("Line1Plc.MonitorStatus_ns2_s_A_B_2_Event", names[underscored].MonitorStatusEventTypeFullName);
        }

        [TestMethod]
        public void TryGetMonitorStatusEventFor_MonitorAndDelete_DerivesStatusName()
        {
            Assert.IsTrue(MessageNames.TryGetMonitorStatusEventFor("Line1Plc.Monitor_ns2_s_X_2_Command", "Line1Plc", out var fromMonitor));
            Assert.AreEqual("Line1Plc.MonitorStatus_ns2_s_X_2_Event", fromMonitor);
            Assert.IsTrue(MessageNames.TryGetMonitorStatusEventFor("Line1Plc.Delete_ns2_s_X_Command", "Line1Plc", out var fromDelete));
            Assert.AreEqual("Line1Plc.MonitorStatus_ns2_s_X_Event", fromDelete);
            Assert.IsFalse(MessageNames.TryGetMonitorStatusEventFor("Line1Plc.Get_ns2_s_X_Command", "Line1Plc", out _));
            Assert.IsFalse(MessageNames.TryGetMonitorStatusEventFor("Other.Monitor_ns2_s_X_Command", "Line1Plc", out _));
            Assert.IsFalse(MessageNames.TryGetMonitorStatusEventFor("Line1Plc.Monitor_ns2_s_X_Event", "Line1Plc", out _));
        }

        [TestMethod]
        public void SelfDescription_ConsumedLikeExplorer_PerItemMonitorMessagesEmit()
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
            Assert.IsNull(monitor.GetProperty("MonitorName"));
            CollectionAssert.AreEqual(new[] { "Disabled", "Reporting" }, Enum.GetNames(monitor.GetProperty("MonitoringMode")!.PropertyType));
            CollectionAssert.AreEqual(new[] { "Status", "StatusValue", "StatusValueTimestamp" }, Enum.GetNames(monitor.GetProperty("DataChangeTrigger")!.PropertyType));
            CollectionAssert.AreEqual(new[] { "None", "Absolute", "Percent" }, Enum.GetNames(monitor.GetProperty("DeadbandType")!.PropertyType));
            Assert.AreEqual(typeof(double), monitor.GetProperty("DeadbandValue")!.PropertyType);
            Assert.AreEqual(typeof(double), monitor.GetProperty("SamplingInterval")!.PropertyType);
            Assert.AreEqual(typeof(uint), monitor.GetProperty("QueueSize")!.PropertyType);
            Assert.AreEqual(typeof(double), monitor.GetProperty("PublishingInterval")!.PropertyType);

            var delete = emitted[temperature.DeleteCommandTypeFullName];
            Assert.IsTrue(typeof(StatePipes.Interfaces.ICommand).IsAssignableFrom(delete));
            Assert.IsEmpty(delete.GetProperties());

            var status = emitted[temperature.MonitorStatusEventTypeFullName];
            Assert.IsTrue(typeof(StatePipes.Interfaces.IEvent).IsAssignableFrom(status));
            CollectionAssert.AreEqual(new[] { "Active", "Pending", "Failed", "Suspended", "Deleted" }, Enum.GetNames(status.GetProperty("State")!.PropertyType));
            Assert.AreEqual(typeof(string), status.GetProperty("NodeId")!.PropertyType);
            Assert.AreEqual(typeof(uint), status.GetProperty("StatusCode")!.PropertyType);
            Assert.AreEqual(typeof(string), status.GetProperty("Status")!.PropertyType);
            Assert.AreEqual(typeof(double), status.GetProperty("RevisedSamplingInterval")!.PropertyType);
            Assert.AreEqual(typeof(uint), status.GetProperty("RevisedQueueSize")!.PropertyType);
            Assert.AreEqual(typeof(double), status.GetProperty("RevisedPublishingInterval")!.PropertyType);
            Assert.IsNull(status.GetProperty("MonitorName"));

            Assert.IsFalse(emitted.Keys.Any(name => name.EndsWith("DeleteMonitorCommand") || name.EndsWith("MonitorStatusEvent")));
            Assert.IsTrue(emitted.Keys.Where(name => name.Contains("_ns2_s_Line1_Temperature_")).All(name => name.StartsWith("Line1Plc.")));
        }
    }
}
