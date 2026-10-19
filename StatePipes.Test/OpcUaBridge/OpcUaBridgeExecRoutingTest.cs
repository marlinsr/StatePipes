using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.Common;
using StatePipes.Comms;
using StatePipes.OpcUaBridge;
using System.Collections.Concurrent;
using System.Text;
using static StatePipes.Test.OpcUaBridge.OpcUaBridgeMonitorTest;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>How the bridge routes Exec commands and answers them, with a fake OPC UA session and broker.</summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class OpcUaBridgeExecRoutingTest
    {
        /// <summary>Records calls; a call waits for <see cref="CallGate"/> when set and returns sum 5.</summary>
        private sealed class RecordingSession : IOpcUaSession
        {
            public ConcurrentQueue<(NodeId ObjectId, NodeId MethodId, IReadOnlyList<Variant> Inputs)> Calls { get; } = new();
            public Task? CallGate { get; set; }

            public Task<IReadOnlyList<DataValue>> ReadValuesAsync(IReadOnlyList<NodeId> nodeIds, CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<DataValue>>([.. nodeIds.Select(_ => DataValue.FromStatusCode(StatusCodes.BadNotConnected))]);

            public Task<IReadOnlyList<StatusCode>> WriteValuesAsync(IReadOnlyList<(NodeId NodeId, Variant Value)> values, CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<StatusCode>>([.. values.Select(_ => (StatusCode)StatusCodes.Good)]);

            public async Task<CallMethodResult> CallAsync(NodeId objectId, NodeId methodId, IReadOnlyList<Variant> inputs, CancellationToken ct)
            {
                Calls.Enqueue((objectId, methodId, inputs));
                if (CallGate != null) await CallGate.WaitAsync(ct);
                return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = new[] { new Variant(5) } };
            }
        }

        private readonly FakeChannelFactory _channels = new();
        private readonly RecordingSession _session = new();
        private readonly CancellationTokenSource _cts = new();
        private string _directory = string.Empty;
        private OpcUaClient _opcUa = null!;
        private OpcUaBridgeService _bridge = null!;
        private Task _running = Task.CompletedTask;
        private readonly OpcUaMethodItem _add = CreateAdd();
        private readonly OpcUaDataItem _setpoint = CreateSetpoint();

        private static OpcUaMethodItem CreateAdd()
        {
            NodeId objectId = new("Methods", 2), methodId = new("Methods.Add", 2);
            var names = OpcUaNaming.AssignUniqueMethodNames(MonitorRegistryTest.Own.ExchangeNamePrefix, [(objectId, methodId)]).Values.Single();
            return new(objectId, methodId, objectId.ToString(), methodId.ToString(), names,
                [new("a", BuiltInType.Int32, typeof(int), null, false), new("b", BuiltInType.Int32, typeof(int), null, false)],
                [new("sum", BuiltInType.Int32, typeof(int), null, false)]);
        }

        private static OpcUaDataItem CreateSetpoint()
        {
            var nodeId = new NodeId("setpoint", 2);
            return new(nodeId, nodeId.ToString(), OpcUaNaming.ToMessageNames(MonitorRegistryTest.Own.ExchangeNamePrefix, nodeId), typeof(double), null, false, BuiltInType.Double, true);
        }

        [TestInitialize]
        public void Initialize()
        {
            _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "OpcUaBridgeExecRoutingTest", Guid.NewGuid().ToString("N"))).FullName;
            FakeTimeProvider time = new();
            var registry = MonitorRegistry.Load(new MonitorStore(Path.Combine(_directory, "monitors.json")), MonitorRegistryTest.Own, null, time);
            _opcUa = new OpcUaClient(new BridgeSettings
            {
                BusConfig = MonitorRegistryTest.Own,
                OpcUaEndpoint = "opc.tcp://localhost:1/none",
                PkiPath = Path.Combine(_directory, "pki"),
                ApplicationName = "Test"
            });
            _bridge = new OpcUaBridgeService(MonitorRegistryTest.Own, _opcUa, registry, time, _channels.Create, _session);
            _running = _bridge.RunAsync(_cts.Token);
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            await _cts.CancelAsync();
            await _running;
            _bridge.Dispose();
            await _opcUa.DisposeAsync();
            Directory.Delete(_directory, true);
        }

        private void Send(string routingKey, string body, string? replyTo = null) =>
            _channels.Main.CommandConsumer!(Encoding.UTF8.GetBytes(body), routingKey, Encoding.UTF8.GetBytes(replyTo ?? ClientReplyTo));

        /// <summary>What the bridge sent to the client, leaving out its own heartbeats.</summary>
        private List<(byte[] Body, string RoutingKey, string ReplyToRaw, string ExchangeName)> Responses() =>
            [.. _channels.Main.Sent.Where(s => s.ExchangeName == Client.ResponseExchangeName)];

        private List<(byte[] Body, string RoutingKey, string ReplyToRaw, string ExchangeName)> WaitForSent(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Responses().Count < count && DateTime.UtcNow < deadline) Thread.Sleep(10);
            var responses = Responses();
            Assert.HasCount(count, responses);
            return responses;
        }

        private static JObject Body(byte[] body) => JObject.Parse(Encoding.UTF8.GetString(body));

        [TestMethod]
        public void Exec_Known_CalledAndAnswered()
        {
            _bridge.SetCatalog([], [_add]);

            Send(_add.ExecCommandTypeFullName, """{"a":2,"b":3}""");

            var sent = WaitForSent(1).Single();
            Assert.AreEqual(_add.ExecResultEventTypeFullName, sent.RoutingKey);
            Assert.AreEqual(Client.ResponseExchangeName, sent.ExchangeName);
            Assert.AreEqual(ClientReplyTo, sent.ReplyToRaw);
            Assert.AreEqual(5, (int)Body(sent.Body)["sum"]!);
            var call = _session.Calls.Single();
            Assert.AreEqual(_add.MethodId, call.MethodId);
            Assert.AreEqual(_add.ObjectId, call.ObjectId);
        }

        [TestMethod]
        public void Exec_BeforeDiscovery_BadWaitingForInitialData()
        {
            Send(_add.ExecCommandTypeFullName, """{"a":2,"b":3}""");

            var sent = WaitForSent(1).Single();
            Assert.AreEqual(_add.ExecResultEventTypeFullName, sent.RoutingKey);
            Assert.AreEqual(StatusCodes.BadWaitingForInitialData.Code, (uint)Body(sent.Body)["StatusCode"]!);
            Assert.IsEmpty(_session.Calls);
        }

        [TestMethod]
        public void Exec_Unknown_BadMethodInvalidWithEmptyIds()
        {
            _bridge.SetCatalog([], []);

            Send(_add.ExecCommandTypeFullName, """{"a":2,"b":3}""");

            var body = Body(WaitForSent(1).Single().Body);
            Assert.AreEqual(StatusCodes.BadMethodInvalid.Code, (uint)body["StatusCode"]!);
            Assert.AreEqual(string.Empty, (string)body["ObjectId"]!);
            Assert.AreEqual(string.Empty, (string)body["MethodId"]!);
            Assert.IsEmpty(_session.Calls);
        }

        [TestMethod]
        public void Exec_GuardFails_NoCallNoAnswer()
        {
            _bridge.SetCatalog([_setpoint], [_add]);
            var otherCert = JsonUtility.GetJsonStringForObject(new BusConfig(Client.BrokerUri, "Client1", "other.p12", "p.txt"), true);

            Send(_add.ExecCommandTypeFullName, """{"a":2,"b":3}""", otherCert);
            Send(_setpoint.CommandTypeFullName, "{}"); // a Get after it, answered once the bridge has handled both

            Assert.AreEqual(_setpoint.EventTypeFullName, WaitForSent(1).Single().RoutingKey);
            Assert.IsEmpty(_session.Calls);
        }

        [TestMethod]
        public void RefusalsAnsweredInArrivalOrder()
        {
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _session.CallGate = gate.Task;
            _bridge.SetCatalog([], [_add]);
            var unknownMethod = OpcUaNaming.ToMethodMessageNames(MonitorRegistryTest.Own.ExchangeNamePrefix, null, new NodeId("Methods.Gone", 2));

            Send(_add.ExecCommandTypeFullName, """{"a":2,"b":3}""");
            Send(unknownMethod.ExecCommandTypeFullName, "{}");
            Send(_setpoint.SetCommandTypeFullName, """{"Value":1}""");
            Thread.Sleep(200);
            Assert.IsEmpty(Responses(), "Refusals waited for the call ahead of them");

            gate.SetResult();

            CollectionAssert.AreEqual(new[] { _add.ExecResultEventTypeFullName, unknownMethod.ExecResultEventTypeFullName, _setpoint.SetStatusEventTypeFullName },
                WaitForSent(3).Select(s => s.RoutingKey).ToArray());
        }
    }
}
