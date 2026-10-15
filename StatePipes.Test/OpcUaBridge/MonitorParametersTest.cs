using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.OpcUaBridge;
using StatePipes.OpcUaBridge.Messages;
using System.Text;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class MonitorParametersTest
    {
        private static JObject Baseline() => new()
        {
            ["MonitorName"] = "m1",
            ["MonitoringMode"] = 1,
            ["DataChangeTrigger"] = "StatusValue",
            ["DeadbandType"] = 1,
            ["DeadbandValue"] = 0.5,
            ["SamplingInterval"] = 100,
            ["QueueSize"] = 2,
            ["PublishingInterval"] = 1000
        };

        private static ParsedMonitorCommand Parse(Action<JObject>? change = null)
        {
            var body = Baseline();
            change?.Invoke(body);
            return MonitorParameters.Parse(Encoding.UTF8.GetBytes(body.ToString()));
        }

        private static void AssertInvalid(ParsedMonitorCommand parsed)
        {
            Assert.AreEqual(StatusCodes.BadInvalidArgument, parsed.Status.Code);
            Assert.IsNull(parsed.Parameters);
        }

        [TestMethod]
        public void Parse_Valid_ReturnsParameters()
        {
            var parsed = Parse();
            Assert.AreEqual(StatusCodes.Good, parsed.Status.Code);
            Assert.AreEqual("m1", parsed.MonitorName);
            Assert.AreEqual(new MonitorParameters(MonitoringModeKind.Reporting, DataChangeTriggerKind.StatusValue, DeadbandTypeKind.Absolute, 0.5, 100, 2, 1000), parsed.Parameters);
        }

        [TestMethod]
        public void Parse_EmptyOrLongName_Invalid()
        {
            AssertInvalid(Parse(b => b["MonitorName"] = ""));
            AssertInvalid(Parse(b => b["MonitorName"] = new string('x', 257)));
            Assert.AreEqual(StatusCodes.Good, Parse(b => b["MonitorName"] = new string('x', 256)).Status.Code);
        }

        [TestMethod]
        public void Parse_NegativeSampling_Invalid()
        {
            AssertInvalid(Parse(b => b["SamplingInterval"] = -1));
            Assert.AreEqual(StatusCodes.Good, Parse(b => b["SamplingInterval"] = 0).Status.Code);
        }

        [TestMethod]
        public void Parse_NonPositivePublishing_Invalid() => AssertInvalid(Parse(b => b["PublishingInterval"] = 0));

        [TestMethod]
        public void Parse_ZeroQueue_Invalid() => AssertInvalid(Parse(b => b["QueueSize"] = 0));

        [TestMethod]
        public void Parse_PercentOver100_Invalid()
        {
            AssertInvalid(Parse(b => { b["DeadbandType"] = "Percent"; b["DeadbandValue"] = 100.1; }));
            Assert.AreEqual(StatusCodes.Good, Parse(b => { b["DeadbandType"] = "Percent"; b["DeadbandValue"] = 100; }).Status.Code);
        }

        [TestMethod]
        public void Parse_NegativeDeadband_Invalid() => AssertInvalid(Parse(b => { b["DeadbandType"] = "Absolute"; b["DeadbandValue"] = -0.1; }));

        [TestMethod]
        public void Parse_NoneDeadband_ValueIgnored()
        {
            var parsed = Parse(b => { b["DeadbandType"] = "None"; b["DeadbandValue"] = -5; });
            Assert.AreEqual(StatusCodes.Good, parsed.Status.Code);
            Assert.AreEqual(0.0, parsed.Parameters!.DeadbandValue);
        }

        [TestMethod]
        public void Parse_UndefinedEnum_Invalid()
        {
            AssertInvalid(Parse(b => b["MonitoringMode"] = 7));
            AssertInvalid(Parse(b => b["DeadbandType"] = "Bogus"));
        }

        [TestMethod]
        public void Parse_MissingField_Invalid() => AssertInvalid(Parse(b => b.Remove("QueueSize")));

        [TestMethod]
        public void Parse_Garbage_InvalidNoThrow()
        {
            var parsed = MonitorParameters.Parse(Encoding.UTF8.GetBytes("{not json"));
            AssertInvalid(parsed);
            Assert.AreEqual("", parsed.MonitorName);
        }

        [TestMethod]
        public void ReadMonitorName_InvalidParametersStillNamed()
        {
            Assert.AreEqual("m1", MonitorParameters.ReadMonitorName(Encoding.UTF8.GetBytes("{\"MonitorName\":\"m1\",\"QueueSize\":0}")));
            Assert.IsNull(MonitorParameters.ReadMonitorName(Encoding.UTF8.GetBytes("garbage")));
        }
    }
}
