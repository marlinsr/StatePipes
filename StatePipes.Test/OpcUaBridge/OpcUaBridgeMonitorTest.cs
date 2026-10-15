using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.BrokerProxy;
using StatePipes.Common;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.OpcUaBridge;
using StatePipes.OpcUaBridge.Messages;
using System.Collections.Concurrent;
using System.Text;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>A broker channel that records what is sent and lets a test deliver commands.</summary>
    internal sealed class FakeConnectionChannel : ISimpleConnectionChannel
    {
        public bool Open { get; set; } = true;
        public bool IsOpen => Open;
        public SimpleMessageReceived? CommandConsumer { get; private set; }
        public ConcurrentQueue<(byte[] Body, string RoutingKey, string ReplyToRaw, string ExchangeName)> Sent { get; } = new();
        /// <summary>Exchange names that no longer exist on the "broker".</summary>
        public ConcurrentDictionary<string, bool> MissingBuses { get; } = new();

        public void ConfigureBus(Guid id, CommunicationsType commsType, string exchangeName, SimpleMessageReceived? consumeMethod = null, List<string>? routingKeys = null, bool autoDelete = false)
        {
            if (commsType == CommunicationsType.Command) CommandConsumer = consumeMethod;
        }

        public void Send(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw, string exchangeName) =>
            Sent.Enqueue((body, routingKey, Encoding.UTF8.GetString(replyToRaw.Span), exchangeName));

        public Task<bool> BusExistsAsync(string exchangeName, CancellationToken ct) => Task.FromResult(!MissingBuses.ContainsKey(exchangeName));

        public void Dispose() => Open = false;
    }

    /// <summary>Hands out <see cref="FakeConnectionChannel"/>s: the first is the bridge's main channel, the second its notification channel.</summary>
    internal sealed class FakeChannelFactory
    {
        public List<FakeConnectionChannel> Created { get; } = [];

        public ISimpleConnectionChannel Create(BusConfig busConfig, Action<ISimpleConnectionChannel>? configureBuses, CancellationToken ct)
        {
            FakeConnectionChannel channel = new();
            lock (Created) Created.Add(channel);
            configureBuses?.Invoke(channel);
            return channel;
        }

        public FakeConnectionChannel Main => Wait(0);
        public FakeConnectionChannel Notifications => Wait(1);

        private FakeConnectionChannel Wait(int index)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (true)
            {
                lock (Created) if (Created.Count > index) return Created[index];
                if (DateTime.UtcNow > deadline) Assert.Fail($"Bridge never created channel {index}");
                Thread.Sleep(10);
            }
        }
    }

    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class OpcUaBridgeMonitorTest
    {
        internal static readonly BusConfig Client = new("amqps://broker", "Client1", "c.p12", "p.txt");
        internal static readonly string ClientReplyTo = JsonUtility.GetJsonStringForObject(Client, true);
        private static readonly string StatusEventName = typeof(MonitorStatusEvent).FullName!;

        private readonly FakeTimeProvider _time = new();
        private readonly FakeChannelFactory _channels = new();
        private readonly CancellationTokenSource _cts = new();
        private string _directory = string.Empty;
        private MonitorRegistry _registry = null!;
        private OpcUaClient _opcUa = null!;
        private OpcUaBridgeService _bridge = null!;
        private Task _running = Task.CompletedTask;
        private OpcUaDataItem _pressure = null!;

        [TestInitialize]
        public void Initialize()
        {
            _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "OpcUaBridgeMonitorTest", Guid.NewGuid().ToString("N"))).FullName;
            _registry = MonitorRegistry.Load(new MonitorStore(Path.Combine(_directory, "monitors.json")), MonitorRegistryTest.Own, null, _time);
            var settings = new BridgeSettings
            {
                BusConfig = MonitorRegistryTest.Own,
                OpcUaEndpoint = "opc.tcp://localhost:1/none",
                PkiPath = Path.Combine(_directory, "pki"),
                ApplicationName = "Test"
            };
            _opcUa = new OpcUaClient(settings);
            _bridge = new OpcUaBridgeService(MonitorRegistryTest.Own, _opcUa, _registry, _time, _channels.Create);
            _running = _bridge.RunAsync(_cts.Token);
            var nodeId = new NodeId("pressure", 2);
            _pressure = new(nodeId, nodeId.ToString(), OpcUaNaming.ToMessageNames(MonitorRegistryTest.Own.ExchangeNamePrefix, nodeId), typeof(double));
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

        private static byte[] MonitorBody(string name = "m1", uint queueSize = 1) => Encoding.UTF8.GetBytes(new JObject
        {
            ["MonitorName"] = name,
            ["MonitoringMode"] = "Reporting",
            ["DataChangeTrigger"] = "StatusValue",
            ["DeadbandType"] = "None",
            ["DeadbandValue"] = 0,
            ["SamplingInterval"] = 100,
            ["QueueSize"] = queueSize,
            ["PublishingInterval"] = 1000
        }.ToString());

        private void SendCommand(string routingKey, byte[] body, string? replyTo = null) =>
            _channels.Main.CommandConsumer!(body, routingKey, Encoding.UTF8.GetBytes(replyTo ?? ClientReplyTo));

        private static (byte[] Body, string RoutingKey, string ReplyToRaw, string ExchangeName) Next(FakeConnectionChannel channel, string routingKey)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var match = channel.Sent.FirstOrDefault(s => s.RoutingKey == routingKey);
                if (match.Body != null) return match;
                Thread.Sleep(10);
            }
            Assert.Fail($"Nothing sent with routing key {routingKey}");
            return default;
        }

        private MonitorStatusEvent NextStatus()
        {
            var sent = Next(_channels.Notifications, StatusEventName);
            Assert.AreEqual(Client.ResponseExchangeName, sent.ExchangeName);
            Assert.AreEqual(ClientReplyTo, sent.ReplyToRaw);
            return JsonUtility.GetObjectForJsonString<MonitorStatusEvent>(Encoding.UTF8.GetString(sent.Body))!;
        }

        [TestMethod]
        public void MonitorCommand_BeforeDiscovery_FailedWaitingForInitialData()
        {
            SendCommand(_pressure.MonitorCommandTypeFullName, MonitorBody());
            var status = NextStatus();
            Assert.AreEqual("m1", status.MonitorName);
            Assert.AreEqual(MonitorState.Failed, status.State);
            Assert.AreEqual(StatusCodes.BadWaitingForInitialData, status.StatusCode);
            Assert.AreEqual("", status.NodeId);
            Assert.IsEmpty(_registry.GetAll());
        }

        [TestMethod]
        public void MonitorCommand_UnknownAfterDiscovery_FailedNodeIdUnknown()
        {
            _bridge.SetCatalog([]);
            SendCommand(_pressure.MonitorCommandTypeFullName, MonitorBody());
            var status = NextStatus();
            Assert.AreEqual(StatusCodes.BadNodeIdUnknown, status.StatusCode);
            Assert.AreEqual("", status.NodeId);
        }

        [TestMethod]
        public void MonitorCommand_Invalid_FailedInvalidArgumentNothingStored()
        {
            _bridge.SetCatalog([_pressure]);
            SendCommand(_pressure.MonitorCommandTypeFullName, MonitorBody(queueSize: 0));
            var status = NextStatus();
            Assert.AreEqual(MonitorState.Failed, status.State);
            Assert.AreEqual(StatusCodes.BadInvalidArgument, status.StatusCode);
            Assert.AreEqual(_pressure.NodeIdText, status.NodeId);
            Assert.IsEmpty(_registry.GetAll());
        }

        [TestMethod]
        public void MonitorCommand_Valid_ReachesCoordinatorAndRespondsOnNotificationChannel()
        {
            _bridge.SetCatalog([_pressure]);
            SendCommand(_pressure.MonitorCommandTypeFullName, MonitorBody());
            var status = NextStatus();
            Assert.AreEqual(MonitorState.Pending, status.State);
            var stored = _registry.Get(new MonitorKey(Client.CommandExchangeName, "m1"));
            Assert.IsNotNull(stored);
            Assert.AreEqual(_pressure.NodeIdText, stored.NodeIdText);
            Assert.IsFalse(_channels.Main.Sent.Any(s => s.RoutingKey == StatusEventName));
        }

        [TestMethod]
        public void MonitorCommand_OtherBroker_DroppedNoResponse()
        {
            _bridge.SetCatalog([_pressure]);
            var elsewhere = JsonUtility.GetJsonStringForObject(new BusConfig("amqps://elsewhere", "Client1", "c.p12", "p.txt"), true);
            SendCommand(_pressure.MonitorCommandTypeFullName, MonitorBody(), elsewhere);
            Thread.Sleep(300);
            Assert.IsEmpty(_channels.Notifications.Sent);
            Assert.IsEmpty(_registry.GetAll());
        }

        [TestMethod]
        public void DeleteMonitorCommand_BeforeDiscovery_Processed()
        {
            SendCommand(typeof(DeleteMonitorCommand).FullName!, Encoding.UTF8.GetBytes("{\"MonitorName\":\"m1\"}"));
            var status = NextStatus();
            Assert.AreEqual(MonitorState.Failed, status.State);
            Assert.AreEqual(StatusCodes.BadNotFound, status.StatusCode);
        }

        [TestMethod]
        public void DeleteMonitorCommand_Existing_Deleted()
        {
            _bridge.SetCatalog([_pressure]);
            SendCommand(_pressure.MonitorCommandTypeFullName, MonitorBody());
            NextStatus();
            _channels.Notifications.Sent.Clear();
            SendCommand(typeof(DeleteMonitorCommand).FullName!, Encoding.UTF8.GetBytes("{\"MonitorName\":\"m1\"}"));
            Assert.AreEqual(MonitorState.Deleted, NextStatus().State);
            Assert.IsEmpty(_registry.GetAll());
        }

        [TestMethod]
        public void GetCommand_StillAnsweredOnMainChannel()
        {
            _bridge.SetCatalog([_pressure]);
            SendCommand(_pressure.CommandTypeFullName, []);
            var sent = Next(_channels.Main, _pressure.EventTypeFullName);
            Assert.AreEqual(Client.ResponseExchangeName, sent.ExchangeName);
            StringAssert.Contains(Encoding.UTF8.GetString(sent.Body), "BadNotConnected");
        }

        [TestMethod]
        public void Notification_SentAsGetEventOnNotificationChannel()
        {
            _bridge.SetCatalog([_pressure]);
            _bridge.Monitors.SessionStarted(new FakeMonitorSession(), [_pressure]);
            SendCommand(_pressure.MonitorCommandTypeFullName, MonitorBody());
            Assert.AreEqual(MonitorState.Active, NextStatus().State);
            _bridge.Monitors.OnNotification(new MonitorKey(Client.CommandExchangeName, "m1"), new DataValue(new Variant(42.0)));
            var sent = Next(_channels.Notifications, _pressure.EventTypeFullName);
            Assert.AreEqual(Client.ResponseExchangeName, sent.ExchangeName);
            Assert.AreEqual(42.0, (double)JObject.Parse(Encoding.UTF8.GetString(sent.Body))["Value"]!);
        }

        [TestMethod]
        public void NotificationChannelClosed_TriggersReplyToCheck()
        {
            _bridge.SetCatalog([_pressure]);
            _bridge.Monitors.SessionStarted(new FakeMonitorSession(), [_pressure]);
            SendCommand(_pressure.MonitorCommandTypeFullName, MonitorBody());
            NextStatus();
            var notifications = _channels.Notifications;
            notifications.Open = false;
            notifications.MissingBuses[Client.ResponseExchangeName] = true;
            var key = new MonitorKey(Client.CommandExchangeName, "m1");
            _bridge.Monitors.OnNotification(key, new DataValue(new Variant(1.0)));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_registry.Get(key)!.IsSuspended)
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("Monitor was not suspended");
                Thread.Sleep(10);
            }
        }
    }
}
