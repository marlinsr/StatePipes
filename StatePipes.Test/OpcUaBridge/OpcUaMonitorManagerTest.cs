using Opc.Ua;
using StatePipes.OpcUaBridge;
using StatePipes.OpcUaBridge.Messages;
using System.Collections.Concurrent;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class OpcUaMonitorManagerTest
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
        private static PlcServerFixture _plc = null!;
        private OpcUaClient _client = null!;
        private IMonitorSession _session = null!;
        private readonly ConcurrentDictionary<MonitorKey, BlockingCollection<DataValue>> _notifications = new();
        private double _pressure;

        [ClassInitialize]
        public static async Task ClassInitialize(TestContext _) => _plc = await PlcServerFixture.StartAsync(48401);

        [ClassCleanup]
        public static async Task ClassCleanup() => await _plc.DisposeAsync();

        [TestInitialize]
        public async Task Initialize()
        {
            _pressure = 1000 * Random.Shared.Next(1, 1000);
            _plc.NodeManager.UpdatePressure(_pressure);
            _client = new OpcUaClient(_plc.CreateBridgeSettings(MonitorRegistryTest.Own));
            await _client.ConnectAsync(CancellationToken.None);
            _session = _client.CreateMonitorManager((key, value) => Values(key).Add(value));
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            await _session.DisposeAsync();
            await _client.DisposeAsync();
        }

        private BlockingCollection<DataValue> Values(MonitorKey key) => _notifications.GetOrAdd(key, _ => []);

        private DataValue Next(MonitorKey key)
        {
            Assert.IsTrue(Values(key).TryTake(out var value, Timeout), $"No notification for {key} within {Timeout}");
            return value;
        }

        private void AssertNoNotification(MonitorKey key, double seconds = 1.5) =>
            Assert.IsFalse(Values(key).TryTake(out var value, TimeSpan.FromSeconds(seconds)), $"Unexpected notification {value.WrappedValue}");

        private static MonitorDefinition Definition(string name, NodeId nodeId, DeadbandTypeKind deadbandType = DeadbandTypeKind.None, double deadband = 0,
            double publishingInterval = 100, MonitoringModeKind mode = MonitoringModeKind.Reporting) =>
            MonitorStoreTest.Definition(name, nodeId.ToString()) with
            {
                Parameters = new MonitorParameters(mode, DataChangeTriggerKind.StatusValue, deadbandType, deadband, 50, 10, publishingInterval)
            };

        private void SetPressure(double delta)
        {
            _pressure += delta;
            _plc.NodeManager.UpdatePressure(_pressure);
        }

        [TestMethod]
        public async Task Apply_Reporting_ActiveWithRevisedValuesAndNotifies()
        {
            var definition = Definition("a", _plc.Pressure);
            var result = await _session.ApplyAsync(definition, CancellationToken.None);
            Assert.AreEqual(MonitorState.Active, result.State);
            Assert.AreEqual(StatusCodes.Good, result.StatusCode.Code);
            Assert.IsGreaterThan(0.0, result.RevisedPublishingInterval);
            Assert.IsGreaterThan(0u, result.RevisedQueueSize);
            Assert.AreEqual(_pressure, Next(definition.Key).WrappedValue.TryGetValue(out double initial) ? initial : double.NaN);
            SetPressure(1);
            Assert.AreEqual(_pressure, Next(definition.Key).WrappedValue.TryGetValue(out double changed) ? changed : double.NaN);
        }

        [TestMethod]
        public async Task Apply_AbsoluteDeadband_SuppressesSmallChanges()
        {
            var definition = Definition("a", _plc.Pressure, DeadbandTypeKind.Absolute, 1.0);
            await _session.ApplyAsync(definition, CancellationToken.None);
            Next(definition.Key);
            SetPressure(0.5);
            AssertNoNotification(definition.Key);
            SetPressure(1.5);
            Assert.AreEqual(_pressure, Next(definition.Key).WrappedValue.TryGetValue(out double value) ? value : double.NaN);
        }

        [TestMethod]
        public async Task Apply_PercentOnNonAnalogNode_FailedWithServerCode()
        {
            var result = await _session.ApplyAsync(Definition("a", _plc.Pressure, DeadbandTypeKind.Percent, 10), CancellationToken.None);
            Assert.AreEqual(MonitorState.Failed, result.State);
            // The stack answers BadDeadbandFilterInvalid (servers may also use BadFilterNotAllowed); the bridge passes it on.
            Assert.AreEqual(StatusCodes.BadDeadbandFilterInvalid, result.StatusCode.Code);
        }

        [TestMethod]
        public async Task Apply_UnknownNode_FailedNodeIdUnknown()
        {
            var result = await _session.ApplyAsync(Definition("a", new NodeId("nope", _plc.NamespaceIndex)), CancellationToken.None);
            Assert.AreEqual(MonitorState.Failed, result.State);
            Assert.AreEqual(StatusCodes.BadNodeIdUnknown, result.StatusCode.Code);
        }

        [TestMethod]
        public async Task Apply_SameKeyNewPublishingInterval_MovesSubscriptionAndKeepsNotifying()
        {
            var definition = Definition("a", _plc.Pressure, publishingInterval: 1000);
            await _session.ApplyAsync(definition, CancellationToken.None);
            Next(definition.Key);
            var result = await _session.ApplyAsync(Definition("a", _plc.Pressure, publishingInterval: 250), CancellationToken.None);
            Assert.AreEqual(MonitorState.Active, result.State);
            // The server revises 250 ms to its own granularity; the item must now be in a faster subscription.
            Assert.IsLessThan(1000.0, result.RevisedPublishingInterval);
            while (Values(definition.Key).TryTake(out _, TimeSpan.FromMilliseconds(500))) { }
            SetPressure(1);
            Assert.AreEqual(_pressure, Next(definition.Key).WrappedValue.TryGetValue(out double value) ? value : double.NaN);
            AssertNoNotification(definition.Key);
        }

        [TestMethod]
        public async Task Apply_SameKeyNewDeadband_ModifiedInPlace()
        {
            var definition = Definition("a", _plc.Pressure);
            await _session.ApplyAsync(definition, CancellationToken.None);
            Next(definition.Key);
            var result = await _session.ApplyAsync(Definition("a", _plc.Pressure, DeadbandTypeKind.Absolute, 1.0), CancellationToken.None);
            Assert.AreEqual(MonitorState.Active, result.State);
            while (Values(definition.Key).TryTake(out _, TimeSpan.FromMilliseconds(500))) { }
            SetPressure(0.5);
            AssertNoNotification(definition.Key);
        }

        [TestMethod]
        public async Task Apply_SameKeyNewNode_NotificationsOnlyFromNewNode()
        {
            var definition = Definition("a", _plc.Pressure);
            await _session.ApplyAsync(definition, CancellationToken.None);
            Next(definition.Key);
            await _session.ApplyAsync(Definition("a", _plc.Msg), CancellationToken.None);
            Assert.IsTrue(Next(definition.Key).WrappedValue.TryGetValue(out string _));
            SetPressure(1);
            AssertNoNotification(definition.Key);
            _plc.NodeManager.UpdateMsg("changed " + _pressure);
            Assert.AreEqual("changed " + _pressure, Next(definition.Key).WrappedValue.TryGetValue(out string text) ? text : null);
        }

        [TestMethod]
        public async Task Remove_StopsNotifications()
        {
            var definition = Definition("a", _plc.Pressure);
            await _session.ApplyAsync(definition, CancellationToken.None);
            Next(definition.Key);
            await _session.RemoveAsync(definition.Key, CancellationToken.None);
            SetPressure(1);
            AssertNoNotification(definition.Key);
        }

        [TestMethod]
        public async Task Disable_StopsNotifications()
        {
            var definition = Definition("a", _plc.Pressure);
            await _session.ApplyAsync(definition, CancellationToken.None);
            Next(definition.Key);
            await _session.DisableAsync(definition.Key, CancellationToken.None);
            SetPressure(1);
            AssertNoNotification(definition.Key);
        }

        [TestMethod]
        public async Task ApplyAll_GroupsByPublishingInterval()
        {
            MonitorDefinition[] definitions =
            [
                Definition("a", _plc.Pressure, publishingInterval: 100),
                Definition("b", _plc.Msg, publishingInterval: 100),
                Definition("c", _plc.Pressure, publishingInterval: 200)
            ];
            var results = await _session.ApplyAllAsync(definitions, CancellationToken.None);
            Assert.HasCount(3, results);
            foreach (var definition in definitions)
            {
                Assert.AreEqual(MonitorState.Active, results[definition.Key].State, definition.Key.ToString());
                Next(definition.Key);
            }
            Assert.AreEqual(200.0, results[definitions[2].Key].RevisedPublishingInterval);
        }

        [TestMethod]
        public async Task ApplyAll_OneBadNode_OthersActive()
        {
            MonitorDefinition[] definitions = [Definition("a", _plc.Pressure), Definition("b", new NodeId("nope", _plc.NamespaceIndex))];
            var results = await _session.ApplyAllAsync(definitions, CancellationToken.None);
            Assert.AreEqual(MonitorState.Active, results[definitions[0].Key].State);
            Assert.AreEqual(StatusCodes.BadNodeIdUnknown, results[definitions[1].Key].StatusCode.Code);
        }

        [TestMethod]
        public async Task Disabled_CreatedButSilent()
        {
            var definition = Definition("a", _plc.Pressure, mode: MonitoringModeKind.Disabled);
            var result = await _session.ApplyAsync(definition, CancellationToken.None);
            Assert.AreEqual(MonitorState.Active, result.State);
            SetPressure(1);
            AssertNoNotification(definition.Key);
        }
    }
}
