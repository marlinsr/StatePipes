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
    /// <summary>How the bridge routes Set commands and answers them, with a fake OPC UA writer and broker.</summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class OpcUaBridgeSetRoutingTest
    {
        private sealed class RecordingSetSession : IOpcUaSession
        {
            public ConcurrentQueue<(NodeId NodeId, Variant Value)> Written { get; } = new();

            public Task<IReadOnlyList<DataValue>> ReadValuesAsync(IReadOnlyList<NodeId> nodeIds, CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<DataValue>>([.. nodeIds.Select(_ => DataValue.FromStatusCode(StatusCodes.BadNotConnected))]);

            public Task<IReadOnlyList<StatusCode>> WriteValuesAsync(IReadOnlyList<(NodeId NodeId, Variant Value)> values, CancellationToken ct)
            {
                foreach (var value in values) Written.Enqueue(value);
                return Task.FromResult<IReadOnlyList<StatusCode>>([.. values.Select(_ => (StatusCode)StatusCodes.Good)]);
            }

            public Task<CallMethodResult> CallAsync(NodeId objectId, NodeId methodId, IReadOnlyList<Variant> inputs, CancellationToken ct) =>
                Task.FromResult(new CallMethodResult { StatusCode = StatusCodes.BadNotConnected });
        }

        private readonly FakeChannelFactory _channels = new();
        private readonly RecordingSetSession _session = new();
        private readonly CancellationTokenSource _cts = new();
        private string _directory = string.Empty;
        private OpcUaClient _opcUa = null!;
        private OpcUaBridgeService _bridge = null!;
        private Task _running = Task.CompletedTask;
        private readonly OpcUaDataItem _setpoint = Item("setpoint", isWritable: true);
        private readonly OpcUaDataItem _readOnly = Item("readOnly", isWritable: false);

        private static OpcUaDataItem Item(string name, bool isWritable)
        {
            var nodeId = new NodeId(name, 2);
            return new(nodeId, nodeId.ToString(), OpcUaNaming.ToMessageNames(MonitorRegistryTest.Own.ExchangeNamePrefix, nodeId), typeof(double), null, false, BuiltInType.Double, isWritable);
        }

        [TestInitialize]
        public void Initialize()
        {
            _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "OpcUaBridgeSetRoutingTest", Guid.NewGuid().ToString("N"))).FullName;
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

        private JObject NextSetStatus(OpcUaDataItem item)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var sent = _channels.Main.Sent.FirstOrDefault(s => s.RoutingKey == item.SetStatusEventTypeFullName);
                if (sent.Body != null)
                {
                    Assert.AreEqual(Client.ResponseExchangeName, sent.ExchangeName);
                    Assert.AreEqual(ClientReplyTo, sent.ReplyToRaw);
                    return JObject.Parse(Encoding.UTF8.GetString(sent.Body));
                }
                Thread.Sleep(10);
            }
            Assert.Fail($"No {item.SetStatusEventTypeFullName} sent");
            return null!;
        }

        private static void AssertStatus(JObject status, StatusCode statusCode, string nodeId)
        {
            Assert.AreEqual(statusCode.Code, (uint)status["StatusCode"]!);
            Assert.AreEqual(nodeId, (string)status["NodeId"]!);
            Assert.IsFalse(string.IsNullOrEmpty((string?)status["Status"]));
        }

        [TestMethod]
        public void Set_WritableItem_WrittenAndAnsweredOnMainChannel()
        {
            _bridge.SetCatalog([_setpoint, _readOnly]);

            Send(_setpoint.SetCommandTypeFullName, """{"Value":4.5}""");

            AssertStatus(NextSetStatus(_setpoint), StatusCodes.Good, "ns=2;s=setpoint");
            var written = _session.Written.Single();
            Assert.AreEqual(_setpoint.NodeId, written.NodeId);
            Assert.AreEqual(4.5, written.Value.AsBoxedObject(Variant.BoxingBehavior.Legacy));
        }

        [TestMethod]
        public void Set_BeforeDiscovery_BadWaitingForInitialData()
        {
            Send(_setpoint.SetCommandTypeFullName, """{"Value":4.5}""");

            AssertStatus(NextSetStatus(_setpoint), StatusCodes.BadWaitingForInitialData, "");
            Assert.IsEmpty(_session.Written);
        }

        [TestMethod]
        public void Set_UnknownNode_BadNodeIdUnknown()
        {
            _bridge.SetCatalog([_readOnly]);

            Send(_setpoint.SetCommandTypeFullName, """{"Value":4.5}""");

            AssertStatus(NextSetStatus(_setpoint), StatusCodes.BadNodeIdUnknown, "");
            Assert.IsEmpty(_session.Written);
        }

        [TestMethod]
        public void Set_ReadableButNotWritable_BadNotWritable()
        {
            _bridge.SetCatalog([_setpoint, _readOnly]);

            Send(_readOnly.SetCommandTypeFullName, """{"Value":4.5}""");

            AssertStatus(NextSetStatus(_readOnly), StatusCodes.BadNotWritable, "ns=2;s=readOnly");
            Assert.IsEmpty(_session.Written);
        }

        [TestMethod]
        public void Set_ReplyToFailsGuard_NoWriteNoAnswer()
        {
            _bridge.SetCatalog([_setpoint]);
            var otherCert = JsonUtility.GetJsonStringForObject(new BusConfig(Client.BrokerUri, "Client1", "other.p12", "p.txt"), true);

            Send(_setpoint.SetCommandTypeFullName, """{"Value":4.5}""", otherCert);
            Send(_setpoint.CommandTypeFullName, "{}"); // a Get after it, answered once the bridge has handled both
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_channels.Main.Sent.Any(s => s.RoutingKey == _setpoint.EventTypeFullName) && DateTime.UtcNow < deadline) Thread.Sleep(10);

            Assert.IsEmpty(_session.Written);
            Assert.IsFalse(_channels.Main.Sent.Any(s => s.RoutingKey.Contains(".SetStatus_")));
        }

        [TestMethod]
        public void MonitorCommand_UnknownNode_StillAnsweredWithMonitorStatus()
        {
            _bridge.SetCatalog([]);

            Send(_setpoint.MonitorCommandTypeFullName, "{}");

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_channels.Notifications.Sent.Any(s => s.RoutingKey == _setpoint.MonitorStatusEventTypeFullName) && DateTime.UtcNow < deadline) Thread.Sleep(10);
            Assert.IsTrue(_channels.Notifications.Sent.Any(s => s.RoutingKey == _setpoint.MonitorStatusEventTypeFullName));
        }
    }
}
