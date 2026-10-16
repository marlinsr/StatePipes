using Newtonsoft.Json.Linq;
using StatePipes.Messages;
using StatePipes.OpcUaBridge;
using StatePipes.OpcUaBridge.Messages;
using StatePipes.Common;
using System.Text;
using static StatePipes.Test.OpcUaBridge.OpcUaBridgeMonitorTest;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>
    /// The whole bridge -- OPC UA session loop, coordinator, routing -- against the in-process PLC, with a fake
    /// broker channel standing in for RabbitMQ or Kafka.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class OpcUaBridgeMonitorRecoveryTest
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(60);
        private static PlcServerFixture _plc = null!;
        private string _directory = string.Empty;
        private Bridge? _bridge;
        private double _pressure;

        /// <summary>One running bridge: its channels, registry and the tasks running it.</summary>
        private sealed class Bridge : IAsyncDisposable
        {
            private readonly CancellationTokenSource _cts = new();
            private readonly OpcUaClient _opcUa;
            private readonly Task _running;

            public Bridge(PlcServerFixture plc, string storePath)
            {
                var settings = plc.CreateBridgeSettings(MonitorRegistryTest.Own);
                Registry = MonitorRegistry.Load(new MonitorStore(storePath), MonitorRegistryTest.Own, null, TimeProvider.System);
                _opcUa = new OpcUaClient(settings);
                Service = new OpcUaBridgeService(MonitorRegistryTest.Own, _opcUa, Registry, TimeProvider.System, Channels.Create);
                _running = Task.WhenAll(Service.RunAsync(_cts.Token), ServiceWorker.MaintainOpcUaSessionAsync(settings, _opcUa, Service, _cts.Token));
            }

            public FakeChannelFactory Channels { get; } = new();
            public MonitorRegistry Registry { get; }
            public OpcUaBridgeService Service { get; }

            public async ValueTask DisposeAsync()
            {
                await _cts.CancelAsync();
                await _running;
                Service.Dispose();
                await _opcUa.DisposeAsync();
            }
        }

        [ClassInitialize]
        public static async Task ClassInitialize(TestContext _) => _plc = await PlcServerFixture.StartAsync(48402);

        [ClassCleanup]
        public static async Task ClassCleanup() => await _plc.DisposeAsync();

        [TestInitialize]
        public void Initialize()
        {
            _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "OpcUaBridgeMonitorRecoveryTest", Guid.NewGuid().ToString("N"))).FullName;
            SetPressure(1000 * Random.Shared.Next(1, 1000));
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (_bridge != null) await _bridge.DisposeAsync();
            Directory.Delete(_directory, true);
        }

        private string StorePath => Path.Combine(_directory, "monitors.json");
        private string MonitorCommand => OpcUaNaming.ToMessageNames(MonitorRegistryTest.Own.ExchangeNamePrefix, _plc.Pressure).MonitorCommandTypeFullName;
        private string GetCommand => OpcUaNaming.ToMessageNames(MonitorRegistryTest.Own.ExchangeNamePrefix, _plc.Pressure).CommandTypeFullName;
        private string GetEvent => OpcUaNaming.ToMessageNames(MonitorRegistryTest.Own.ExchangeNamePrefix, _plc.Pressure).EventTypeFullName;
        private string DeleteCommand => OpcUaNaming.ToMessageNames(MonitorRegistryTest.Own.ExchangeNamePrefix, _plc.Pressure).DeleteCommandTypeFullName;
        private string StatusEvent => OpcUaNaming.ToMessageNames(MonitorRegistryTest.Own.ExchangeNamePrefix, _plc.Pressure).MonitorStatusEventTypeFullName;
        private MonitorKey Key => new(Client.CommandExchangeName, _plc.Pressure.ToString());

        private void SetPressure(double value)
        {
            _pressure = value;
            _plc.NodeManager.UpdatePressure(value);
        }

        private static void Send(Bridge bridge, string routingKey, byte[] body) =>
            bridge.Channels.Main.CommandConsumer!(body, routingKey, Encoding.UTF8.GetBytes(ClientReplyTo));

        private static byte[] MonitorBody => Encoding.UTF8.GetBytes(new JObject
        {
            ["MonitoringMode"] = "Reporting",
            ["DataChangeTrigger"] = "StatusValue",
            ["DeadbandType"] = "None",
            ["DeadbandValue"] = 0,
            ["SamplingInterval"] = 50,
            ["QueueSize"] = 10,
            ["PublishingInterval"] = 100
        }.ToString());

        private static T Poll<T>(Func<T?> probe, TimeSpan timeout, string what) where T : class
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var result = probe();
                if (result != null) return result;
                Thread.Sleep(20);
            }
            Assert.Fail($"Timed out waiting for {what}");
            return null!;
        }

        /// <summary>Waits for the first discovery by asking for the self description until it lists the pressure node.</summary>
        private Bridge StartBridge()
        {
            Bridge bridge = new(_plc, StorePath);
            var selfDescription = typeof(SelfDescriptionEvent).FullName!;
            Poll(() =>
            {
                Send(bridge, typeof(GetSelfDescriptionCommand).FullName!, []);
                Thread.Sleep(200);
                return bridge.Channels.Main.Sent.Any(s => s.RoutingKey == selfDescription && Encoding.UTF8.GetString(s.Body).Contains(MonitorCommand)) ? bridge : null;
            }, RecoveryTimeout, "discovery");
            return bridge;
        }

        private JObject TakeStatus(Bridge bridge, TimeSpan timeout)
        {
            var name = StatusEvent;
            return Poll(() =>
            {
                while (bridge.Channels.Notifications.Sent.TryDequeue(out var sent))
                    if (sent.RoutingKey == name) return JObject.Parse(Encoding.UTF8.GetString(sent.Body));
                return null;
            }, timeout, "a monitor status");
        }

        private static void AssertState(JObject status, MonitorState state) => Assert.AreEqual(state.ToString(), (string)status["State"]!, (string?)status["Status"]);

        private void AssertGetEventWithCurrentPressure(Bridge bridge)
        {
            var expected = _pressure;
            Poll(() =>
            {
                while (bridge.Channels.Notifications.Sent.TryDequeue(out var sent))
                {
                    if (sent.RoutingKey != GetEvent) continue;
                    Assert.AreEqual(Client.ResponseExchangeName, sent.ExchangeName);
                    if ((double?)JObject.Parse(Encoding.UTF8.GetString(sent.Body))["Value"] == expected) return sent.Body;
                }
                return null;
            }, Timeout, $"a Get event with pressure {expected}");
        }

        private JObject CreateMonitor(Bridge bridge)
        {
            Send(bridge, MonitorCommand, MonitorBody);
            var status = TakeStatus(bridge, Timeout);
            AssertState(status, MonitorState.Active);
            return status;
        }

        [TestMethod]
        public void Monitor_EndToEnd_GetEventsOnChange()
        {
            _bridge = StartBridge();
            var status = CreateMonitor(_bridge);
            Assert.AreEqual(_plc.Pressure.ToString(), (string)status["NodeId"]!);
            AssertGetEventWithCurrentPressure(_bridge);
            SetPressure(_pressure + 1);
            AssertGetEventWithCurrentPressure(_bridge);
        }

        [TestMethod]
        public void Delete_EndToEnd_StopsNotifications()
        {
            _bridge = StartBridge();
            CreateMonitor(_bridge);
            Send(_bridge, DeleteCommand, []);
            AssertState(TakeStatus(_bridge, Timeout), MonitorState.Deleted);
            Assert.IsNull(_bridge.Registry.Get(Key));
            _bridge.Channels.Notifications.Sent.Clear();
            SetPressure(_pressure + 1);
            Thread.Sleep(1000);
            Assert.IsFalse(_bridge.Channels.Notifications.Sent.Any(s => s.RoutingKey == GetEvent), "A deleted monitor still notified");
        }

        [TestMethod]
        public async Task ServerReboot_MonitorsRebuiltWithStatusAndNotifications()
        {
            _bridge = StartBridge();
            CreateMonitor(_bridge);
            await _plc.RestartAsync();
            SetPressure(_pressure);
            var status = TakeStatus(_bridge, RecoveryTimeout);
            AssertState(status, MonitorState.Active);
            Assert.AreEqual(_plc.Pressure.ToString(), (string)status["NodeId"]!);
            SetPressure(_pressure + 1);
            AssertGetEventWithCurrentPressure(_bridge);
        }

        [TestMethod]
        public async Task BridgeRestart_MonitorsRestoredFromFile()
        {
            var first = StartBridge();
            CreateMonitor(first);
            await first.DisposeAsync();

            _bridge = new Bridge(_plc, StorePath);
            AssertState(TakeStatus(_bridge, RecoveryTimeout), MonitorState.Active);
            SetPressure(_pressure + 1);
            AssertGetEventWithCurrentPressure(_bridge);
        }

        [TestMethod]
        public void MissingReplyTo_Suspended_GetsStillServed_ReclaimRestores()
        {
            _bridge = StartBridge();
            CreateMonitor(_bridge);
            _bridge.Channels.Notifications.MissingBuses[Client.ResponseExchangeName] = true;
            _bridge.Service.Monitors.CheckReplyTos();
            Poll(() => _bridge.Registry.Get(Key)!.IsSuspended ? "suspended" : null, Timeout, "suspension");
            StringAssert.Contains(File.ReadAllText(StorePath), "\"suspendedSince\": \"");

            _bridge.Channels.Notifications.Sent.Clear();
            SetPressure(_pressure + 1);
            Thread.Sleep(1000);
            Assert.IsFalse(_bridge.Channels.Notifications.Sent.Any(s => s.RoutingKey == GetEvent), "A suspended monitor still notified");

            Send(_bridge, GetCommand, []);
            Poll(() => _bridge.Channels.Main.Sent.Any(s => s.RoutingKey == GetEvent) ? "answered" : null, Timeout, "a Get response");

            _bridge.Channels.Notifications.MissingBuses.Clear();
            CreateMonitor(_bridge);
            Assert.IsFalse(_bridge.Registry.Get(Key)!.IsSuspended);
            SetPressure(_pressure + 1);
            AssertGetEventWithCurrentPressure(_bridge);
        }
    }
}
