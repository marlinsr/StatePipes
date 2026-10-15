using Microsoft.Extensions.Time.Testing;
using Opc.Ua;
using StatePipes.Comms;
using StatePipes.OpcUaBridge;
using StatePipes.OpcUaBridge.Messages;
using System.Collections.Concurrent;
using static StatePipes.Test.OpcUaBridge.MonitorStoreTest;

namespace StatePipes.Test.OpcUaBridge
{
    internal sealed class FakeMonitorSession : IMonitorSession
    {
        public ConcurrentDictionary<MonitorKey, MonitorApplyResult> Results { get; } = new();
        public ConcurrentQueue<MonitorDefinition> Applied { get; } = new();
        public ConcurrentQueue<IReadOnlyList<MonitorDefinition>> AppliedAll { get; } = new();
        public ConcurrentQueue<MonitorKey> Removed { get; } = new();
        public ConcurrentQueue<MonitorKey> Disabled { get; } = new();
        public bool Disposed { get; private set; }
        /// <summary>When set, Remove and Disable throw it, as a session dying underneath them might.</summary>
        public Exception? Throw { get; set; }
        public static MonitorApplyResult ActiveResult { get; } = new(MonitorState.Active, StatusCodes.Good, 50, 10, 100);

        private MonitorApplyResult ResultFor(MonitorKey key) => Results.GetValueOrDefault(key, ActiveResult);

        public Task<MonitorApplyResult> ApplyAsync(MonitorDefinition definition, CancellationToken ct)
        {
            Applied.Enqueue(definition);
            return Task.FromResult(ResultFor(definition.Key));
        }

        public Task<IReadOnlyDictionary<MonitorKey, MonitorApplyResult>> ApplyAllAsync(IReadOnlyList<MonitorDefinition> definitions, CancellationToken ct)
        {
            AppliedAll.Enqueue(definitions);
            return Task.FromResult<IReadOnlyDictionary<MonitorKey, MonitorApplyResult>>(definitions.ToDictionary(d => d.Key, d => ResultFor(d.Key)));
        }

        public Task RemoveAsync(MonitorKey key, CancellationToken ct)
        {
            Removed.Enqueue(key);
            return Throw == null ? Task.CompletedTask : Task.FromException(Throw);
        }

        public Task DisableAsync(MonitorKey key, CancellationToken ct)
        {
            Disabled.Enqueue(key);
            return Throw == null ? Task.CompletedTask : Task.FromException(Throw);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class FakeMonitorOutput : IMonitorOutput
    {
        public ConcurrentQueue<(MonitorStatusEvent Status, string ReplyToRaw)> Statuses { get; } = new();
        public ConcurrentQueue<(OpcUaDataItem Item, DataValue Value, string ReplyToRaw)> Notifications { get; } = new();
        /// <summary>Response exchange GUIDs whose bus no longer exists.</summary>
        public ConcurrentDictionary<string, bool> Missing { get; } = new();
        public ConcurrentQueue<string> ExistenceChecks { get; } = new();

        public void SendStatus(MonitorStatusEvent status, string replyToRaw) => Statuses.Enqueue((status, replyToRaw));
        public void SendNotification(OpcUaDataItem item, DataValue value, string replyToRaw) => Notifications.Enqueue((item, value, replyToRaw));

        public Task<bool> ReplyToExistsAsync(BusConfig replyTo, CancellationToken ct)
        {
            ExistenceChecks.Enqueue(replyTo.ResponseExchangeGuid);
            return Task.FromResult(!Missing.ContainsKey(replyTo.ResponseExchangeGuid));
        }
    }

    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class MonitorCoordinatorTest
    {
        private const string Pressure = "ns=2;s=pressure";
        private const string Msg = "ns=2;s=msg";
        private static readonly OpcUaDataItem PressureItem = Item(Pressure, typeof(double));
        private static readonly OpcUaDataItem MsgItem = Item(Msg, typeof(string));
        private static readonly IReadOnlyList<OpcUaDataItem> Catalog = [PressureItem, MsgItem];

        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero));
        private readonly FakeMonitorOutput _output = new();
        private readonly CancellationTokenSource _cts = new();
        private string _directory = string.Empty;
        private MonitorRegistry _registry = null!;
        private MonitorCoordinator _coordinator = null!;
        private Task _running = Task.CompletedTask;

        private static OpcUaDataItem Item(string nodeIdText, Type type)
        {
            var nodeId = NodeId.Parse(nodeIdText);
            return new(nodeId, nodeIdText, OpcUaNaming.ToMessageNames("Line1Plc", nodeId), type);
        }

        [TestInitialize]
        public void Initialize()
        {
            _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MonitorCoordinatorTest", Guid.NewGuid().ToString("N"))).FullName;
            Start(TimeSpan.FromHours(1));
        }

        private void Start(TimeSpan? expiry)
        {
            _registry = MonitorRegistry.Load(new MonitorStore(Path.Combine(_directory, "monitors.json")), MonitorRegistryTest.Own, expiry, _time);
            _coordinator = new MonitorCoordinator(_registry, _output, _time);
            _running = _coordinator.RunAsync(_cts.Token);
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            await _cts.CancelAsync();
            await _running;
            Directory.Delete(_directory, true);
        }

        private static void WaitUntil(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail($"Timed out waiting for {what}");
                Thread.Sleep(10);
            }
        }

        private MonitorStatusEvent NextStatus(out string replyToRaw)
        {
            (MonitorStatusEvent Status, string ReplyToRaw) entry = default;
            WaitUntil(() => _output.Statuses.TryDequeue(out entry), "a status");
            replyToRaw = entry.ReplyToRaw;
            return entry.Status;
        }

        private MonitorStatusEvent NextStatus() => NextStatus(out _);

        private void AssertNoMoreStatuses()
        {
            Thread.Sleep(200);
            Assert.IsEmpty(_output.Statuses);
        }

        private FakeMonitorSession StartSession()
        {
            FakeMonitorSession session = new();
            _coordinator.SessionStarted(session, Catalog);
            return session;
        }

        private void Monitor(MonitorDefinition definition, OpcUaDataItem item) => _coordinator.Monitor(definition.Key, item, definition.Parameters, definition.ReplyToRaw);

        [TestMethod]
        public void Monitor_Connected_UpsertsAppliesAndRespondsActive()
        {
            var session = StartSession();
            var definition = Definition("a");
            Monitor(definition, PressureItem);
            var status = NextStatus(out var replyTo);
            Assert.AreEqual("a", status.MonitorName);
            Assert.AreEqual(Pressure, status.NodeId);
            Assert.AreEqual(MonitorState.Active, status.State);
            Assert.AreEqual(50.0, status.RevisedSamplingInterval);
            Assert.AreEqual(10u, status.RevisedQueueSize);
            Assert.AreEqual(100.0, status.RevisedPublishingInterval);
            Assert.AreEqual(definition.ReplyToRaw, replyTo);
            Assert.AreEqual(definition, _registry.Get(definition.Key));
            Assert.HasCount(1, session.Applied);
        }

        [TestMethod]
        public void Monitor_Disconnected_StoredPendingResponse()
        {
            var definition = Definition("a");
            Monitor(definition, PressureItem);
            var status = NextStatus();
            Assert.AreEqual(MonitorState.Pending, status.State);
            Assert.AreEqual(StatusCodes.BadNotConnected, status.StatusCode);
            Assert.IsNotNull(_registry.Get(definition.Key));
        }

        [TestMethod]
        public void Monitor_SessionFails_StoredFailedResponseWithCode()
        {
            var session = StartSession();
            var definition = Definition("a");
            session.Results[definition.Key] = MonitorApplyResult.Failed(StatusCodes.BadDeadbandFilterInvalid);
            Monitor(definition, PressureItem);
            var status = NextStatus();
            Assert.AreEqual(MonitorState.Failed, status.State);
            Assert.AreEqual(StatusCodes.BadDeadbandFilterInvalid, status.StatusCode);
            Assert.IsNotNull(_registry.Get(definition.Key));
        }

        [TestMethod]
        public void Monitor_SameKeyNewReplyTo_NotificationsGoToNewReplyToOnly()
        {
            StartSession();
            var first = Definition("a");
            var second = first with { ReplyToRaw = first.ReplyToRaw.Replace("abc", "new") };
            Monitor(first, PressureItem);
            NextStatus();
            Monitor(second, PressureItem);
            NextStatus();
            _coordinator.OnNotification(first.Key, Pressure, new DataValue(new Variant(1.5)));
            Assert.HasCount(1, _output.Notifications);
            Assert.AreEqual(second.ReplyToRaw, _output.Notifications.Single().ReplyToRaw);
        }

        [TestMethod]
        public void Monitor_SuspendedKey_Reclaimed()
        {
            var session = StartSession();
            var definition = Definition("a");
            Monitor(definition, PressureItem);
            NextStatus();
            _registry.Suspend(definition.Key);
            Monitor(definition, PressureItem);
            Assert.AreEqual(MonitorState.Active, NextStatus().State);
            Assert.IsFalse(_registry.Get(definition.Key)!.IsSuspended);
            Assert.HasCount(2, session.Applied);
        }

        [TestMethod]
        public void Delete_Existing_RemovedRespondsDeleted()
        {
            var session = StartSession();
            var definition = Definition("a");
            Monitor(definition, PressureItem);
            NextStatus();
            var deleteReplyTo = definition.ReplyToRaw.Replace("abc", "del");
            _coordinator.Delete(definition.Key, deleteReplyTo);
            var status = NextStatus(out var replyTo);
            Assert.AreEqual(MonitorState.Deleted, status.State);
            Assert.AreEqual(Pressure, status.NodeId);
            Assert.AreEqual(deleteReplyTo, replyTo);
            Assert.IsNull(_registry.Get(definition.Key));
            CollectionAssert.AreEqual(new[] { definition.Key }, session.Removed.ToArray());
        }

        [TestMethod]
        public void Delete_Unknown_RespondsFailedBadNotFound()
        {
            _coordinator.Delete(new MonitorKey("Client1.commands", "nope"), Definition("x").ReplyToRaw);
            var status = NextStatus();
            Assert.AreEqual(MonitorState.Failed, status.State);
            Assert.AreEqual(StatusCodes.BadNotFound, status.StatusCode);
            Assert.AreEqual("", status.NodeId);
        }

        [TestMethod]
        public void Delete_WhileDisconnected_RemovedAndNotRebuilt()
        {
            var definition = Definition("a");
            Monitor(definition, PressureItem);
            NextStatus();
            _coordinator.Delete(definition.Key, definition.ReplyToRaw);
            NextStatus();
            var session = StartSession();
            WaitUntil(() => !session.AppliedAll.IsEmpty, "rebuild");
            Assert.IsEmpty(session.AppliedAll.Single());
            AssertNoMoreStatuses();
        }

        [TestMethod]
        public void SessionStarted_RebuildsNonSuspendedAndSendsStatusToEach()
        {
            MonitorDefinition a = Definition("a"), b = Definition("b", Msg), c = Definition("c");
            _registry.Upsert(a); _registry.Upsert(b); _registry.Upsert(c);
            _registry.Suspend(c.Key);
            var session = StartSession();
            var statuses = new[] { NextStatus(), NextStatus() };
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, statuses.Select(s => s.MonitorName).ToArray());
            Assert.IsTrue(statuses.All(s => s.State == MonitorState.Active));
            CollectionAssert.AreEquivalent(new[] { a.Key, b.Key }, session.AppliedAll.Single().Select(d => d.Key).ToArray());
            AssertNoMoreStatuses();
        }

        [TestMethod]
        public void SessionStarted_NodeMissingFromCatalog_FailedBadNodeIdUnknown()
        {
            var gone = Definition("gone", "ns=2;s=removed");
            _registry.Upsert(gone);
            var session = StartSession();
            var status = NextStatus();
            Assert.AreEqual(MonitorState.Failed, status.State);
            Assert.AreEqual(StatusCodes.BadNodeIdUnknown, status.StatusCode);
            Assert.AreEqual("ns=2;s=removed", status.NodeId);
            Assert.IsEmpty(session.AppliedAll.Single());
            Assert.IsNotNull(_registry.Get(gone.Key));
        }

        [TestMethod]
        public void SessionStarted_RemovesExpiredSuspended()
        {
            var definition = Definition("a");
            _registry.Upsert(definition);
            _registry.Suspend(definition.Key);
            _time.Advance(TimeSpan.FromMinutes(61));
            var session = StartSession();
            WaitUntil(() => !session.AppliedAll.IsEmpty, "rebuild");
            Assert.IsNull(_registry.Get(definition.Key));
        }

        [TestMethod]
        public void SessionStarted_ThenMonitorQueued_AppliedAfterRebuildOnNewSession()
        {
            var old = StartSession();
            var definition = Definition("a");
            var fresh = StartSession();
            Monitor(definition, PressureItem);
            NextStatus();
            Assert.IsTrue(old.Disposed);
            Assert.IsEmpty(old.Applied);
            Assert.HasCount(1, fresh.Applied);
        }

        [TestMethod]
        public void SessionLost_ThenMonitor_Pending()
        {
            var session = StartSession();
            _coordinator.SessionLost();
            Monitor(Definition("a"), PressureItem);
            Assert.AreEqual(MonitorState.Pending, NextStatus().State);
            Assert.IsTrue(session.Disposed);
            Assert.IsEmpty(session.Applied);
        }

        [TestMethod]
        public void ReplyToCheck_SharedMissingReplyTo_SuspendsEveryMonitorUsingIt()
        {
            var session = StartSession();
            MonitorDefinition a = Definition("a"), b = Definition("b", Msg);
            var live = Definition("c") with { ReplyToRaw = a.ReplyToRaw.Replace("abc", "live") };
            Monitor(a, PressureItem); Monitor(b, MsgItem); Monitor(live, PressureItem);
            NextStatus(); NextStatus(); NextStatus();
            _output.Missing["abc"] = true;
            _coordinator.CheckReplyTos();
            WaitUntil(() => _registry.Get(b.Key)!.IsSuspended && _registry.Get(a.Key)!.IsSuspended && _output.ExistenceChecks.Count == 2, "suspension");
            CollectionAssert.AreEquivalent(new[] { a.Key, b.Key }, session.Disabled.ToArray());
            Assert.IsFalse(_registry.Get(live.Key)!.IsSuspended);
            CollectionAssert.AreEquivalent(new[] { "abc", "live" }, _output.ExistenceChecks.ToArray());
        }

        [TestMethod]
        public void ReplyToCheck_EveryThirtySeconds()
        {
            StartSession();
            var definition = Definition("a");
            Monitor(definition, PressureItem);
            NextStatus();
            _output.Missing["abc"] = true;
            _time.Advance(TimeSpan.FromSeconds(29));
            Thread.Sleep(200);
            Assert.IsFalse(_registry.Get(definition.Key)!.IsSuspended);
            _time.Advance(TimeSpan.FromSeconds(1));
            WaitUntil(() => _registry.Get(definition.Key)!.IsSuspended, "periodic suspension");
        }

        [TestMethod]
        public void Expiry_Hourly_RemovesExpiredWithoutRebuild()
        {
            var session = StartSession();
            var definition = Definition("a");
            Monitor(definition, PressureItem);
            NextStatus();
            _registry.Suspend(definition.Key);
            _time.Advance(TimeSpan.FromMinutes(61));
            Thread.Sleep(100);
            _time.Advance(TimeSpan.FromMinutes(60));
            WaitUntil(() => _registry.Get(definition.Key) == null, "expiry");
            WaitUntil(() => session.Removed.Contains(definition.Key), "removal from session");
        }

        [TestMethod]
        public void OnNotification_SuspendedOrUnknown_NotSent()
        {
            StartSession();
            var definition = Definition("a");
            Monitor(definition, PressureItem);
            NextStatus();
            _registry.Suspend(definition.Key);
            _coordinator.OnNotification(definition.Key, Pressure, new DataValue(new Variant(1.0)));
            _coordinator.OnNotification(new MonitorKey("x", "y"), Pressure, new DataValue(new Variant(1.0)));
            Assert.IsEmpty(_output.Notifications);
        }

        [TestMethod]
        public void OnNotification_Active_SendsGetEventToReplyTo()
        {
            StartSession();
            var definition = Definition("a");
            Monitor(definition, PressureItem);
            NextStatus();
            _coordinator.OnNotification(definition.Key, Pressure, new DataValue(new Variant(2.5)));
            var (item, value, replyTo) = _output.Notifications.Single();
            Assert.AreEqual(PressureItem, item);
            Assert.AreEqual(2.5, value.WrappedValue.TryGetValue(out double d) ? d : double.NaN);
            Assert.AreEqual(definition.ReplyToRaw, replyTo);
        }

        [TestMethod]
        public void Delete_SessionThrowsUnexpected_StillRespondsDeleted()
        {
            var session = StartSession();
            var definition = Definition("a");
            Monitor(definition, PressureItem);
            NextStatus();
            session.Throw = new InvalidOperationException("session is closing");
            _coordinator.Delete(definition.Key, definition.ReplyToRaw);
            Assert.AreEqual(MonitorState.Deleted, NextStatus().State);
        }

        [TestMethod]
        public void ReplyToCheck_DisableThrowsUnexpected_StillSuspendsWholeGroup()
        {
            var session = StartSession();
            MonitorDefinition a = Definition("a"), b = Definition("b", Msg);
            Monitor(a, PressureItem); Monitor(b, MsgItem);
            NextStatus(); NextStatus();
            session.Throw = new InvalidOperationException("session is closing");
            _output.Missing["abc"] = true;
            _coordinator.CheckReplyTos();
            WaitUntil(() => _registry.Get(a.Key)!.IsSuspended && _registry.Get(b.Key)!.IsSuspended, "suspension of both");
        }

        [TestMethod]
        public void OnNotification_FromPreviousNode_Dropped()
        {
            StartSession();
            Monitor(Definition("a"), PressureItem);
            NextStatus();
            var moved = Definition("a", Msg);
            Monitor(moved, MsgItem);
            NextStatus();
            // A pressure notification still in flight while the old item is being deleted must not go out as msg.
            _coordinator.OnNotification(moved.Key, Pressure, new DataValue(new Variant(1.0)));
            Assert.IsEmpty(_output.Notifications);
            _coordinator.OnNotification(moved.Key, Msg, new DataValue(new Variant("text")));
            Assert.AreEqual(MsgItem, _output.Notifications.Single().Item);
        }

        [TestMethod]
        public void Monitor_MovedToOtherNode_RespondsWithNewNode()
        {
            var session = StartSession();
            Monitor(Definition("a"), PressureItem);
            NextStatus();
            var moved = Definition("a", Msg);
            Monitor(moved, MsgItem);
            Assert.AreEqual(Msg, NextStatus().NodeId);
            Assert.AreEqual(Msg, _registry.Get(moved.Key)!.NodeIdText);
            Assert.AreEqual(Msg, session.Applied.Last().NodeIdText);
        }
    }
}
