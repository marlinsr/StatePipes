using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.OpcUaBridge;
using System.Text;
using System.Text.RegularExpressions;
using static StatePipes.Test.OpcUaBridge.OpcUaBridgeMonitorTest;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>Writing OPC UA variables through the bridge, against the in-process PLC's Setpoints nodes.</summary>
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class OpcUaBridgeSetTest
    {
        private static PlcServerFixture _plc = null!;

        [ClassInitialize]
        public static async Task ClassInitialize(TestContext _) => _plc = await PlcServerFixture.StartAsync(48403);

        [ClassCleanup]
        public static async Task ClassCleanup() => await _plc.DisposeAsync();

        private static async Task<IReadOnlyList<OpcUaDataItem>> DiscoverAsync(BridgeSettings? settings = null)
        {
            await using OpcUaClient client = new(settings ?? _plc.CreateBridgeSettings(MonitorRegistryTest.Own));
            await client.ConnectAsync(CancellationToken.None);
            return await client.DiscoverAsync(CancellationToken.None);
        }

        [TestMethod]
        public async Task Discover_Setpoints_ReadableWithNaturalTypes()
        {
            var items = (await DiscoverAsync()).ToDictionary(i => i.NodeId);
            Assert.AreEqual(typeof(short), items[_plc.Setpoint("Int16")].ValueType);
            Assert.AreEqual(typeof(double[]), items[_plc.Setpoint("DoubleArray")].ValueType);
            Assert.IsNotNull(items[_plc.Setpoint("Density")].Structure);
            Assert.IsTrue(items[_plc.Setpoint("DensityArray")].IsArray);
            Assert.AreEqual(typeof(string), items[_plc.Setpoint("Matrix")].ValueType);
            Assert.HasCount(16, items.Keys.Where(n => n.IdentifierAsString?.StartsWith("Setpoints.") == true));
        }

        private static readonly string[] WritableSetpoints = ["Double", "Int16", "Boolean", "String", "DateTime", "DoubleArray", "Int32Array", "Density", "DensityArray", "Tank", "Percent"];
        private static readonly string[] UnwritableSetpoints = ["ReadOnly", "UserReadOnly", "Matrix", "Number", "Text"];

        [TestMethod]
        public async Task Discover_Setpoints_WritableExactlyAsSpecified()
        {
            var items = (await DiscoverAsync()).ToDictionary(i => i.NodeId);
            foreach (var name in WritableSetpoints) Assert.IsTrue(items[_plc.Setpoint(name)].IsWritable, name);
            foreach (var name in UnwritableSetpoints) Assert.IsFalse(items[_plc.Setpoint(name)].IsWritable, name);
            Assert.AreEqual(BuiltInType.Int16, items[_plc.Setpoint("Int16")].BuiltInType);
            Assert.AreEqual(BuiltInType.Double, items[_plc.Setpoint("DoubleArray")].BuiltInType);
        }

        [TestMethod]
        public async Task Discover_WriteFilter_NarrowsWritable()
        {
            var settings = _plc.CreateBridgeSettings(MonitorRegistryTest.Own);
            var filtered = new BridgeSettings
            {
                BusConfig = settings.BusConfig,
                OpcUaEndpoint = settings.OpcUaEndpoint,
                UseSecurity = settings.UseSecurity,
                AutoAcceptUntrustedCertificates = settings.AutoAcceptUntrustedCertificates,
                PkiPath = settings.PkiPath,
                ApplicationName = settings.ApplicationName,
                WriteFilter = new Regex("Setpoints\\.Double$")
            };
            var items = (await DiscoverAsync(filtered)).Where(i => i.NodeId.IdentifierAsString?.StartsWith("Setpoints.") == true).ToList();
            CollectionAssert.AreEqual(new[] { _plc.Setpoint("Double") }, items.Where(i => i.IsWritable).Select(i => i.NodeId).ToArray());
            Assert.IsTrue(items.Any(i => i.NodeId == _plc.Setpoint("ReadOnly")), "Unwritable items stay readable");
        }

        [TestMethod]
        public async Task WriteValues_WritesAndReturnsPerNodeResults()
        {
            await using OpcUaClient client = new(_plc.CreateBridgeSettings(MonitorRegistryTest.Own));
            await client.ConnectAsync(CancellationToken.None);

            var results = await client.WriteValuesAsync([(_plc.Setpoint("Double"), new Variant(42.0)), (_plc.Setpoint("Percent"), new Variant(150.0))], CancellationToken.None);

            CollectionAssert.AreEqual(new[] { StatusCodes.Good, StatusCodes.BadOutOfRange }, results.Select(r => r.Code).ToArray());
            var read = await client.ReadValuesAsync([_plc.Setpoint("Double")], CancellationToken.None);
            Assert.AreEqual(42.0, read[0].WrappedValue.AsBoxedObject(Variant.BoxingBehavior.Legacy));
        }

        [TestMethod]
        public async Task WriteValues_NotConnected_AllBadNotConnected()
        {
            await using OpcUaClient client = new(_plc.CreateBridgeSettings(MonitorRegistryTest.Own));

            var results = await client.WriteValuesAsync([(_plc.Setpoint("Double"), new Variant(1.0)), (_plc.Setpoint("Int16"), new Variant((short)1))], CancellationToken.None);

            CollectionAssert.AreEqual(new[] { StatusCodes.BadNotConnected, StatusCodes.BadNotConnected }, results.Select(r => r.Code).ToArray());
        }

        /// <summary>The whole bridge -- OPC UA session loop, routing, SetWriter -- with a fake broker channel.</summary>
        private sealed class Bridge : IAsyncDisposable
        {
            private readonly CancellationTokenSource _cts = new();
            private readonly OpcUaClient _opcUa;
            private readonly Task _running;
            private readonly string _directory;

            public Bridge()
            {
                _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "OpcUaBridgeSetTest", Guid.NewGuid().ToString("N"))).FullName;
                var settings = _plc.CreateBridgeSettings(MonitorRegistryTest.Own);
                var registry = MonitorRegistry.Load(new MonitorStore(Path.Combine(_directory, "monitors.json")), MonitorRegistryTest.Own, null, TimeProvider.System);
                _opcUa = new OpcUaClient(settings);
                Service = new OpcUaBridgeService(MonitorRegistryTest.Own, _opcUa, registry, TimeProvider.System, Channels.Create);
                _running = Task.WhenAll(Service.RunAsync(_cts.Token), ServiceWorker.MaintainOpcUaSessionAsync(settings, _opcUa, Service, _cts.Token));
            }

            public FakeChannelFactory Channels { get; } = new();
            public OpcUaBridgeService Service { get; }

            public async ValueTask DisposeAsync()
            {
                await _cts.CancelAsync();
                await _running;
                Service.Dispose();
                await _opcUa.DisposeAsync();
                Directory.Delete(_directory, true);
            }
        }

        private static MessageNames Names(string setpoint) => OpcUaNaming.ToMessageNames(MonitorRegistryTest.Own.ExchangeNamePrefix, _plc.Setpoint(setpoint));

        private static void Send(Bridge bridge, string routingKey, string body) =>
            bridge.Channels.Main.CommandConsumer!(Encoding.UTF8.GetBytes(body), routingKey, Encoding.UTF8.GetBytes(ClientReplyTo));

        /// <summary>The bodies sent so far with <paramref name="routingKey"/>, waiting until there are <paramref name="count"/>.</summary>
        private static List<JObject> WaitForResponses(Bridge bridge, string routingKey, int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            List<JObject> responses;
            do
            {
                responses = [.. bridge.Channels.Main.Sent.Where(s => s.RoutingKey == routingKey).Select(s => JObject.Parse(Encoding.UTF8.GetString(s.Body)))];
                if (responses.Count >= count) return responses;
                Thread.Sleep(20);
            }
            while (DateTime.UtcNow < deadline);
            Assert.Fail($"Expected {count} {routingKey}, got {responses.Count}");
            return responses;
        }

        /// <summary>A Get is ignored until the bridge has discovered the server, so asking until one is answered waits for discovery.</summary>
        private static async Task<Bridge> StartBridgeAsync()
        {
            Bridge bridge = new();
            var names = Names("Double");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!bridge.Channels.Main.Sent.Any(s => s.RoutingKey == names.EventTypeFullName))
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("The bridge never discovered the server");
                Send(bridge, names.CommandTypeFullName, "{}");
                await Task.Delay(100);
            }
            return bridge;
        }

        private static JObject Set(Bridge bridge, string setpoint, string valueJson)
        {
            var names = Names(setpoint);
            var before = WaitForResponses(bridge, names.SetStatusEventTypeFullName, 0).Count;
            Send(bridge, names.SetCommandTypeFullName, $$"""{"Value":{{valueJson}}}""");
            return WaitForResponses(bridge, names.SetStatusEventTypeFullName, before + 1)[before];
        }

        private static JToken? Get(Bridge bridge, string setpoint)
        {
            var names = Names(setpoint);
            var before = WaitForResponses(bridge, names.EventTypeFullName, 0).Count;
            Send(bridge, names.CommandTypeFullName, "{}");
            return WaitForResponses(bridge, names.EventTypeFullName, before + 1)[before]["Value"];
        }

        /// <summary>Numbers compared as doubles and dates as UTC instants, as JSON does not keep either distinction.</summary>
        private static JToken? Normalize(JToken? token) => token switch
        {
            JObject o => new JObject(o.Properties().Select(p => new JProperty(p.Name, Normalize(p.Value)))),
            JArray a => new JArray(a.Select(Normalize)),
            JValue { Type: JTokenType.Integer or JTokenType.Float } v => new JValue(Convert.ToDouble(v.Value, System.Globalization.CultureInfo.InvariantCulture)),
            JValue { Type: JTokenType.Date } v => new JValue(((DateTime)v).ToUniversalTime()),
            _ => token
        };

        private static void AssertGood(JObject status) => Assert.AreEqual(((StatusCode)StatusCodes.Good).Code, (uint)status["StatusCode"]!, status.ToString());

        [TestMethod]
        [DataRow("Double", "12.5")]
        [DataRow("Int16", "-7")]
        [DataRow("Boolean", "true")]
        [DataRow("String", "\"hi\"")]
        [DataRow("DateTime", "\"2026-03-04T05:06:07Z\"")]
        [DataRow("DoubleArray", "[1, 2.5]")]
        [DataRow("Int32Array", "[3, 4]")]
        [DataRow("Density", """{"Volume":1,"Mass":2}""")]
        [DataRow("DensityArray", """[{"Volume":1,"Mass":2},{"Volume":3,"Mass":4}]""")]
        [DataRow("Tank", """{"Name":"T2","Density":{"Volume":1,"Mass":2},"History":[{"Volume":3,"Mass":4}],"Readings":[5]}""")]
        // A Get event's value with its null fields left out goes back unchanged.
        [DataRow("Tank", """{"Density":{"Volume":1,"Mass":2},"Readings":[5]}""")]
        public async Task SetThenGet_EachWritableSetpoint_ReturnsWrittenValue(string setpoint, string valueJson)
        {
            await using var bridge = await StartBridgeAsync();

            AssertGood(Set(bridge, setpoint, valueJson));

            var expected = Normalize(JToken.Parse(valueJson));
            var actual = Normalize(Get(bridge, setpoint));
            Assert.IsTrue(JToken.DeepEquals(expected, actual), $"Expected {expected}, got {actual}");
        }

        [TestMethod]
        public async Task SetThenRead_Int16_StaysInt16OnTheServer()
        {
            await using var bridge = await StartBridgeAsync();

            AssertGood(Set(bridge, "Int16", "-8"));

            await using OpcUaClient client = new(_plc.CreateBridgeSettings(MonitorRegistryTest.Own));
            await client.ConnectAsync(CancellationToken.None);
            var read = (await client.ReadValuesAsync([_plc.Setpoint("Int16")], CancellationToken.None))[0];
            Assert.AreEqual(BuiltInType.Int16, read.WrappedValue.TypeInfo.BuiltInType);
            Assert.AreEqual((short)-8, read.WrappedValue.AsBoxedObject(Variant.BoxingBehavior.Legacy));
        }

        [TestMethod]
        public async Task Set_PercentOutOfRange_BadOutOfRangeValueUnchanged()
        {
            await using var bridge = await StartBridgeAsync();

            AssertGood(Set(bridge, "Percent", "50"));
            var refused = Set(bridge, "Percent", "150");

            Assert.AreEqual(((StatusCode)StatusCodes.BadOutOfRange).Code, (uint)refused["StatusCode"]!);
            Assert.AreEqual(50.0, (double)Get(bridge, "Percent")!);
        }

        [TestMethod]
        public async Task Set_TwiceQuickly_LastValueWins()
        {
            await using var bridge = await StartBridgeAsync();
            var names = Names("Double");
            var before = WaitForResponses(bridge, names.SetStatusEventTypeFullName, 0).Count;

            Send(bridge, names.SetCommandTypeFullName, """{"Value":1}""");
            Send(bridge, names.SetCommandTypeFullName, """{"Value":2}""");

            var statuses = WaitForResponses(bridge, names.SetStatusEventTypeFullName, before + 2).Skip(before).ToList();
            statuses.ForEach(AssertGood);
            Assert.AreEqual(2.0, (double)Get(bridge, "Double")!);
        }

        [TestMethod]
        public async Task Set_ReadOnlySetpoint_BadNotWritable()
        {
            await using var bridge = await StartBridgeAsync();

            var status = Set(bridge, "ReadOnly", "1");

            Assert.AreEqual(((StatusCode)StatusCodes.BadNotWritable).Code, (uint)status["StatusCode"]!);
            Assert.AreEqual(_plc.Setpoint("ReadOnly").ToString(), (string)status["NodeId"]!);
        }
    }
}
