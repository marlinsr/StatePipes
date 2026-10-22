using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.OpcUaBridge;
using System.Text;
using System.Text.RegularExpressions;
using static StatePipes.Test.OpcUaBridge.OpcUaBridgeMonitorTest;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>Calling OPC UA methods through the bridge, against the in-process PLC's Methods object.</summary>
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class OpcUaBridgeExecTest
    {
        private static PlcServerFixture _plc = null!;

        [ClassInitialize]
        public static async Task ClassInitialize(TestContext _) => _plc = await PlcServerFixture.StartAsync(48404);

        [ClassCleanup]
        public static async Task ClassCleanup() => await _plc.DisposeAsync();

        private static async Task<OpcUaClient> ConnectAsync()
        {
            OpcUaClient client = new(_plc.CreateBridgeSettings(MonitorRegistryTest.Own));
            await client.ConnectAsync(CancellationToken.None);
            return client;
        }

        private static BridgeSettings Settings(Regex? methodFilter = null, bool includeNamespaceZero = false)
        {
            var settings = _plc.CreateBridgeSettings(MonitorRegistryTest.Own);
            return new BridgeSettings
            {
                BusConfig = settings.BusConfig,
                OpcUaEndpoint = settings.OpcUaEndpoint,
                UseSecurity = settings.UseSecurity,
                AutoAcceptUntrustedCertificates = settings.AutoAcceptUntrustedCertificates,
                PkiPath = settings.PkiPath,
                ApplicationName = settings.ApplicationName,
                MethodFilter = methodFilter,
                IncludeNamespaceZero = includeNamespaceZero
            };
        }

        private static async Task<IReadOnlyList<OpcUaMethodItem>> DiscoverMethodsAsync(BridgeSettings? settings = null)
        {
            await using OpcUaClient client = new(settings ?? Settings());
            await client.ConnectAsync(CancellationToken.None);
            return (await client.DiscoverAsync(CancellationToken.None)).Methods;
        }

        private static bool IsInMethodsObject(OpcUaMethodItem method) => method.MethodId.IdentifierAsString?.StartsWith("Methods.") == true;

        [TestMethod]
        public async Task Discover_MethodsTable_ExecExactlyAsSpec()
        {
            var methods = await DiscoverMethodsAsync();

            CollectionAssert.AreEquivalent(
                new[] { "Add", "Echo", "EchoDensity", "NoArgs", "Slow", "Reject", "TextOut", "Start", "Start" }.Select(n => _plc.Method(n)).ToArray(),
                methods.Where(IsInMethodsObject).Select(m => m.MethodId).ToArray());
            Assert.IsTrue(methods.Any(m => m.MethodId.IdentifierAsString == "CalculatePressureVolumeMass"));
        }

        [TestMethod]
        public async Task Discover_Arguments_Typed()
        {
            var methods = (await DiscoverMethodsAsync()).Where(m => m.ObjectId == _plc.MethodsObject).ToDictionary(m => m.MethodId.IdentifierAsString!);

            var add = methods["Methods.Add"];
            CollectionAssert.AreEqual(new[] { "a", "b" }, add.Inputs.Select(a => a.PropertyName).ToArray());
            Assert.AreEqual(typeof(int), add.Inputs[0].ValueType);
            Assert.AreEqual(BuiltInType.Int32, add.Inputs[0].BuiltInType);
            Assert.AreEqual("sum", add.Outputs.Single().PropertyName);
            var echo = methods["Methods.Echo"].Inputs.ToDictionary(a => a.PropertyName);
            Assert.AreEqual(typeof(double[]), echo["da"].ValueType);
            Assert.IsTrue(echo["da"].IsArray);
            Assert.AreEqual(typeof(byte[]), echo["bytes"].ValueType);
            var echoDensity = methods["Methods.EchoDensity"];
            Assert.IsNotNull(echoDensity.Inputs[0].Structure);
            Assert.IsFalse(echoDensity.Inputs[0].IsArray);
            Assert.IsTrue(echoDensity.Inputs[1].IsArray);
            Assert.AreEqual(typeof(string), methods["Methods.TextOut"].Outputs.Single().ValueType);
            Assert.IsEmpty(methods["Methods.NoArgs"].Inputs);
            Assert.IsEmpty(methods["Methods.NoArgs"].Outputs);
            Assert.AreEqual($"{OpcUaNaming.MessageNamespace(MonitorRegistryTest.Own.ExchangeNamePrefix)}.Exec_ns{_plc.NamespaceIndex}_s_Methods_Add_Command", add.ExecCommandTypeFullName);
        }

        [TestMethod]
        public async Task Discover_SharedMethod_OneExecPerObject()
        {
            var starts = (await DiscoverMethodsAsync()).Where(m => m.MethodId == _plc.Method("Start")).ToList();

            CollectionAssert.AreEquivalent(new[] { _plc.Pump1, _plc.Pump2 }, starts.Select(m => m.ObjectId).ToArray());
            Assert.AreEqual(2, starts.Select(m => m.ExecCommandTypeFullName).Distinct().Count());
            Assert.IsTrue(starts.All(m => m.ExecCommandTypeFullName.Contains("__")), "A shared method is named with its object");
        }

        [TestMethod]
        public async Task Discover_NoServerOwnMethods()
        {
            var methods = await DiscoverMethodsAsync(Settings(includeNamespaceZero: true));

            Assert.IsFalse(methods.Any(m => m.MethodId.NamespaceIndex == 0), string.Join(", ", methods.Where(m => m.MethodId.NamespaceIndex == 0).Select(m => m.MethodIdText)));
            Assert.IsTrue(methods.All(m => m.MethodId.NamespaceIndex == _plc.NamespaceIndex));
        }

        [TestMethod]
        public async Task Discover_MethodFilter_Narrows()
        {
            var methods = await DiscoverMethodsAsync(Settings(methodFilter: new Regex("Methods\\.Add$")));

            CollectionAssert.AreEqual(new[] { _plc.Method("Add") }, methods.Where(IsInMethodsObject).Select(m => m.MethodId).ToArray());
        }

        [TestMethod]
        public async Task Call_Add_ReturnsSum()
        {
            await using var client = await ConnectAsync();

            var result = await client.CallAsync(_plc.MethodsObject, _plc.Method("Add"), [new Variant(2), new Variant(3)], CancellationToken.None);

            Assert.AreEqual(StatusCodes.Good, result.StatusCode.Code, result.StatusCode.ToString());
            Assert.AreEqual(5, result.OutputArguments[0].AsBoxedObject(Variant.BoxingBehavior.Legacy));
        }

        [TestMethod]
        public async Task Call_Slow20Seconds_OutlivesOperationTimeout()
        {
            await using var client = await ConnectAsync();

            var result = await client.CallAsync(_plc.MethodsObject, _plc.Method("Slow"), [new Variant(20_000u)], CancellationToken.None);

            Assert.AreEqual(StatusCodes.Good, result.StatusCode.Code, result.StatusCode.ToString());
        }

        [TestMethod]
        public async Task Call_Reject150_InvalidArgumentWithOutOfRange()
        {
            await using var client = await ConnectAsync();

            var result = await client.CallAsync(_plc.MethodsObject, _plc.Method("Reject"), [new Variant(150.0)], CancellationToken.None);

            Assert.AreEqual(StatusCodes.BadInvalidArgument, result.StatusCode.Code, result.StatusCode.ToString());
            Assert.AreEqual(StatusCodes.BadOutOfRange, result.InputArgumentResults[0].Code);
        }

        [TestMethod]
        public async Task Call_StartOnPump1_Good()
        {
            await using var client = await ConnectAsync();

            var result = await client.CallAsync(_plc.Pump1, _plc.Method("Start"), [], CancellationToken.None);

            Assert.AreEqual(StatusCodes.Good, result.StatusCode.Code, result.StatusCode.ToString());
        }

        [TestMethod]
        public async Task Call_AfterDisconnect_CallSessionClosed()
        {
            await using var client = await ConnectAsync();
            await client.CallAsync(_plc.MethodsObject, _plc.Method("NoArgs"), [], CancellationToken.None);
            await client.DisconnectAsync();

            var result = await client.CallAsync(_plc.MethodsObject, _plc.Method("NoArgs"), [], CancellationToken.None);

            Assert.AreEqual(StatusCodes.BadNotConnected, result.StatusCode.Code);
        }

        [TestMethod]
        public async Task Call_DisconnectDuringSlowCall_Throws()
        {
            await using var client = await ConnectAsync();
            var call = client.CallAsync(_plc.MethodsObject, _plc.Method("Slow"), [new Variant(10_000u)], CancellationToken.None);
            await Task.Delay(500);

            await client.DisconnectAsync();

            var ended = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(call, ended, "The call outlived its session");
            await Assert.ThrowsAsync<ServiceResultException>(() => call);
        }

        [TestMethod]
        public async Task Call_CallSessionClosedByServer_NextCallReopensIt()
        {
            await using var client = await ConnectAsync();
            await client.CallAsync(_plc.MethodsObject, _plc.Method("NoArgs"), [], CancellationToken.None);

            await _plc.CloseSessionsAsync(" calls");
            try { await client.CallAsync(_plc.MethodsObject, _plc.Method("NoArgs"), [], CancellationToken.None); }
            catch (ServiceResultException) { } // the call that finds the session gone: outcome unknown

            var result = await client.CallAsync(_plc.MethodsObject, _plc.Method("NoArgs"), [], CancellationToken.None);
            Assert.AreEqual(StatusCodes.Good, result.StatusCode.Code, result.StatusCode.ToString());
        }

        [TestMethod]
        public async Task Call_DisconnectWhileCallSessionOpens_ReturnsPromptly()
        {
            using FreezingTcpProxy proxy = new(new Uri(_plc.EndpointUrl).Port);
            var endpointUrl = new UriBuilder(_plc.EndpointUrl) { Port = proxy.Port }.Uri.ToString();
            await using OpcUaClient client = new(_plc.CreateBridgeSettings(MonitorRegistryTest.Own, endpointUrl));
            await client.ConnectAsync(CancellationToken.None);
            Assert.IsTrue(proxy.Connections > 0, "The client bypassed the proxy");

            proxy.Freeze();
            var call = client.CallAsync(_plc.MethodsObject, _plc.Method("NoArgs"), [], CancellationToken.None);
            await Task.Delay(1000);
            Assert.IsFalse(call.IsCompleted, "The call session opened through a frozen proxy");
            await client.DisconnectAsync();

            var ended = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(call, ended, "The call session kept opening after the disconnect");
            Assert.AreEqual(StatusCodes.BadNotConnected, (await call).StatusCode.Code);
        }

        [TestMethod]
        public async Task Call_NotConnected_BadNotConnected()
        {
            await using OpcUaClient client = new(_plc.CreateBridgeSettings(MonitorRegistryTest.Own));

            var result = await client.CallAsync(_plc.MethodsObject, _plc.Method("Add"), [new Variant(2), new Variant(3)], CancellationToken.None);

            Assert.AreEqual(StatusCodes.BadNotConnected, result.StatusCode.Code);
        }

        /// <summary>The whole bridge -- OPC UA session loop, routing, CommandExecutor -- with a fake broker channel.</summary>
        private sealed class Bridge : IAsyncDisposable
        {
            private readonly CancellationTokenSource _cts = new();
            private readonly OpcUaClient _opcUa;
            private readonly Task _running;
            private readonly string _directory;

            public Bridge()
            {
                _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "OpcUaBridgeExecTest", Guid.NewGuid().ToString("N"))).FullName;
                var settings = Settings();
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

        private static string Exchange => MonitorRegistryTest.Own.ExchangeNamePrefix;
        private static MessageNames ExecNames(string method, NodeId? qualifyingObject = null) => OpcUaNaming.ToMethodMessageNames(Exchange, qualifyingObject, _plc.Method(method));
        private static MessageNames ItemNames(NodeId nodeId) => OpcUaNaming.ToMessageNames(Exchange, nodeId);

        private static void Send(Bridge bridge, string routingKey, string body) =>
            bridge.Channels.Main.CommandConsumer!(Encoding.UTF8.GetBytes(body), routingKey, Encoding.UTF8.GetBytes(ClientReplyTo));

        /// <summary>Everything sent to the client so far, in order, as (routing key, body).</summary>
        private static List<(string RoutingKey, JObject Body)> Responses(Bridge bridge) =>
            [.. bridge.Channels.Main.Sent.Where(s => s.ExchangeName == Client.ResponseExchangeName).Select(s => (s.RoutingKey, JObject.Parse(Encoding.UTF8.GetString(s.Body))))];

        private static List<JObject> WaitForResponses(Bridge bridge, string routingKey, int count, int timeoutSeconds = 10)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            List<JObject> responses;
            do
            {
                responses = [.. Responses(bridge).Where(r => r.RoutingKey == routingKey).Select(r => r.Body)];
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
            var names = ItemNames(_plc.Method("NoArgsCount"));
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!bridge.Channels.Main.Sent.Any(s => s.RoutingKey == names.EventTypeFullName))
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("The bridge never discovered the server");
                Send(bridge, names.CommandTypeFullName, "{}");
                await Task.Delay(100);
            }
            return bridge;
        }

        private static JObject Exec(Bridge bridge, MessageNames names, string body, int timeoutSeconds = 10)
        {
            var before = WaitForResponses(bridge, names.ExecResultEventTypeFullName, 0).Count;
            Send(bridge, names.ExecCommandTypeFullName, body);
            return WaitForResponses(bridge, names.ExecResultEventTypeFullName, before + 1, timeoutSeconds)[before];
        }

        private static JToken? Get(Bridge bridge, NodeId nodeId)
        {
            var names = ItemNames(nodeId);
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

        private static void AssertGood(JObject result) => Assert.AreEqual(StatusCodes.Good.Code, (uint)result["StatusCode"]!, result.ToString());

        private static void AssertEchoed(JObject inputs, JObject result)
        {
            AssertGood(result);
            foreach (var input in inputs.Properties())
                Assert.IsTrue(JToken.DeepEquals(Normalize(input.Value), Normalize(result[input.Name])), $"{input.Name}: sent {input.Value}, got {result[input.Name]}");
        }

        [TestMethod]
        public async Task Exec_EachMethod_ExpectedOutputs()
        {
            await using var bridge = await StartBridgeAsync();

            var add = Exec(bridge, ExecNames("Add"), """{"a":2,"b":3}""");
            AssertGood(add);
            Assert.AreEqual(5, (int)add["sum"]!);
            Assert.AreEqual(_plc.MethodsObject.ToString(), (string)add["ObjectId"]!);
            Assert.AreEqual(_plc.Method("Add").ToString(), (string)add["MethodId"]!);

            var echoInputs = JObject.Parse("""{"d":1.5,"i16":-7,"b":true,"s":"hi","dt":"2026-03-04T05:06:07Z","da":[1,2.5],"bytes":"AQID"}""");
            AssertEchoed(echoInputs, Exec(bridge, ExecNames("Echo"), echoInputs.ToString()));

            var densityInputs = JObject.Parse("""{"density":{"Volume":1.5,"Mass":2},"densities":[{"Volume":3,"Mass":4},{"Volume":5,"Mass":6}]}""");
            AssertEchoed(densityInputs, Exec(bridge, ExecNames("EchoDensity"), densityInputs.ToString()));

            var noArgs = Exec(bridge, ExecNames("NoArgs"), "{}");
            AssertGood(noArgs);
            CollectionAssert.AreEquivalent(new[] { "ObjectId", "MethodId", "StatusCode", "Status" }, noArgs.Properties().Select(p => p.Name).ToArray());

            var text = Exec(bridge, ExecNames("TextOut"), "{}");
            AssertGood(text);
            Assert.AreEqual(JTokenType.String, text["text"]!.Type);

            var start = Exec(bridge, ExecNames("Start", _plc.Pump1), "{}");
            AssertGood(start);
            Assert.AreEqual(_plc.Pump1.ToString(), (string)start["ObjectId"]!);
        }

        [TestMethod]
        public async Task Exec_Reject150_NamesPercent()
        {
            await using var bridge = await StartBridgeAsync();

            var result = Exec(bridge, ExecNames("Reject"), """{"percent":150}""");

            Assert.AreEqual(StatusCodes.BadInvalidArgument.Code, (uint)result["StatusCode"]!);
            Assert.AreEqual($"{((StatusCode)StatusCodes.BadInvalidArgument).ToString()}: percent ({((StatusCode)StatusCodes.BadOutOfRange).ToString()})", (string)result["Status"]!);
        }

        [TestMethod]
        public async Task Exec_Slow20000_Succeeds()
        {
            await using var bridge = await StartBridgeAsync();

            AssertGood(Exec(bridge, ExecNames("Slow"), """{"ms":20000}""", timeoutSeconds: 40));
        }

        [TestMethod]
        public async Task Ordering_SlowNoArgsGet_AnsweredInOrderAndSeesIncrement()
        {
            await using var bridge = await StartBridgeAsync();
            var count = (int)Get(bridge, _plc.Method("NoArgsCount"))!;
            var slow = ExecNames("Slow");
            var noArgs = ExecNames("NoArgs");
            var counter = ItemNames(_plc.Method("NoArgsCount"));
            var before = Responses(bridge).Count;
            var countsBefore = WaitForResponses(bridge, counter.EventTypeFullName, 0).Count;

            Send(bridge, slow.ExecCommandTypeFullName, """{"ms":2000}""");
            Send(bridge, noArgs.ExecCommandTypeFullName, "{}");
            Send(bridge, counter.CommandTypeFullName, "{}");

            WaitForResponses(bridge, counter.EventTypeFullName, countsBefore + 1, timeoutSeconds: 15);
            var answers = Responses(bridge).Skip(before).ToList();
            CollectionAssert.AreEqual(new[] { slow.ExecResultEventTypeFullName, noArgs.ExecResultEventTypeFullName, counter.EventTypeFullName },
                answers.Select(a => a.RoutingKey).ToArray());
            Assert.AreEqual(count + 1, (int)answers[2].Body["Value"]!);
        }

        [TestMethod]
        public async Task SetThenGet_ReadsOwnWrite()
        {
            await using var bridge = await StartBridgeAsync();
            var names = ItemNames(_plc.Setpoint("Double"));
            var before = WaitForResponses(bridge, names.EventTypeFullName, 0).Count;

            Send(bridge, names.SetCommandTypeFullName, """{"Value":42.5}""");
            Send(bridge, names.CommandTypeFullName, "{}");

            var got = WaitForResponses(bridge, names.EventTypeFullName, before + 1)[before];
            Assert.AreEqual(42.5, (double)got["Value"]!);
        }
    }
}
