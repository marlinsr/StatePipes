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
        public ConcurrentQueue<(MonitorStatus Status, MessageNames Names, string ReplyToRaw)> Statuses { get; } = new();
        public ConcurrentQueue<(OpcUaDataItem Item, DataValue Value, string ReplyToRaw)> Notifications { get; } = new();
        /// <summary>Response exchange GUIDs whose bus no longer exists.</summary>
        public ConcurrentDictionary<string, bool> Missing { get; } = new();
        public ConcurrentQueue<string> ExistenceChecks { get; } = new();

        public void SendStatus(MonitorStatus status, MessageNames names, string replyToRaw) => Statuses.Enqueue((status, names, replyToRaw));
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
        private const string Exchange = "Line1Plc";
        private const string Pressure = "ns=2;s=pressure";
        private const string Msg = "ns=2;s=msg";
        private const string Sender = "Client1.commands";
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
            return new(nodeId, nodeIdText, OpcUaNaming.ToMessageNames(Exchange, nodeId), type);
        }

        [TestInitialize]
        public void Initialize()
        {
            _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MonitorCoordinatorTest", Guid.NewGuid().ToString("N"))).FullName;
            _registry = MonitorRegistry.Load(new MonitorStore(Path.Combine(_directory, "monitors.json")), MonitorRegistryTest.Own, TimeSpan.FromHours(1), _time);
            _coordinator = new MonitorCoordinator(_registry, _output, _time, Exchange);
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

        private (MonitorStatus Status, MessageNames Names, string ReplyToRaw) NextEntry()
        {
            (MonitorStatus, MessageNames, string) entry = default;
            WaitUntil(() => _output.Statuses.TryDequeue(out entry), "a status");
            return entry;
        }

        private MonitorStatus NextStatus() => NextEntry().Status;

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

        private static MonitorDefinition On(string nodeIdText, string sender = "Client1") => Definition(nodeIdText, sender: sender);

        private void Monitor(MonitorDefinition definition, OpcUaDataItem item) =>
            _coordinator.Monitor(definition.Key.SenderCommandExchange, item, definition.Parameters, definition.ReplyToRaw);

        private void Delete(OpcUaDataItem item, string replyToRaw) => _coordinator.Delete(Sender, item, replyToRaw);

        [TestMethod]
        public void Monitor_Connected_UpsertsAppliesAndRespondsActiveOnItemStatusEvent()
        {
            var session = StartSession();
            var definition = On(Pressure);
            Monitor(definition, PressureItem);
            var (status, names, replyTo) = NextEntry();
            Assert.AreEqual(PressureItem.MonitorStatusEventTypeFullName, names.MonitorStatusEventTypeFullName);
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
            var definition = On(Pressure);
            Monitor(definition, PressureItem);
            var status = NextStatus();
            Assert.AreEqual(MonitorState.Pending, status.State);
            Assert.AreEqual(StatusCodes.BadNotConnected, status.StatusCode.Code);
            Assert.IsNotNull(_registry.Get(definition.Key));
        }

        [TestMethod]
        public void Monitor_SessionFails_StoredFailedResponseWithCode()
        {
            var session = StartSession();
            var definition = On(Pressure);
            session.Results[definition.Key] = MonitorApplyResult.Failed(StatusCodes.BadDeadbandFilterInvalid);
            Monitor(definition, PressureItem);
            var status = NextStatus();
            Assert.AreEqual(MonitorState.Failed, status.State);
            Assert.AreEqual(StatusCodes.BadDeadbandFilterInvalid, status.StatusCode.Code);
            Assert.IsNotNull(_registry.Get(definition.Key));
        }

        [TestMethod]
        public void Monitor_SameItemNewReplyTo_NotificationsGoToNewReplyToOnly()
        {
            StartSession();
            var first = On(Pressure);
            var second = first with { ReplyToRaw = first.ReplyToRaw.Replace("abc", "new") };
            Monitor(first, PressureItem);
            NextStatus();
            Monitor(second, PressureItem);
            NextStatus();
            Assert.HasCount(1, _registry.GetAll());
            _coordinator.OnNotification(first.Key, new DataValue(new Variant(1.5)));
            Assert.AreEqual(second.ReplyToRaw, _output.Notifications.Single().ReplyToRaw);
        }

        [TestMethod]
        public void SameItemTwoSenders_EachNotifiedAtOwnReplyTo()
        {
            StartSession();
            var one = On(Pressure, "Client1");
            var two = On(Pressure, "Client2");
            Monitor(one, PressureItem); Monitor(two, PressureItem);
            NextStatus(); NextStatus();
            _coordinator.OnNotification(one.Key, new DataValue(new Variant(1.0)));
            _coordinator.OnNotification(two.Key, new DataValue(new Variant(1.0)));
            CollectionAssert.AreEquivalent(new[] { one.ReplyToRaw, two.ReplyToRaw }, _output.Notifications.Select(n => n.ReplyToRaw).ToArray());
            Assert.AreNotEqual(one.ReplyToRaw, two.ReplyToRaw);
        }

        [TestMethod]
        public void Monitor_SuspendedKey_Reclaimed()
        {
            var session = StartSession();
            var definition = On(Pressure);
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
            var definition = On(Pressure);
            Monitor(definition, PressureItem);
            NextStatus();
            var deleteReplyTo = definition.ReplyToRaw.Replace("abc", "del");
            Delete(PressureItem, deleteReplyTo);
            var (status, names, replyTo) = NextEntry();
            Assert.AreEqual(MonitorState.Deleted, status.State);
            Assert.AreEqual(Pressure, status.NodeId);
            Assert.AreEqual(PressureItem.MonitorStatusEventTypeFullName, names.MonitorStatusEventTypeFullName);
            Assert.AreEqual(deleteReplyTo, replyTo);
            Assert.IsNull(_registry.Get(definition.Key));
            CollectionAssert.AreEqual(new[] { definition.Key }, session.Removed.ToArray());
        }

        [TestMethod]
        public void Delete_NoMonitor_RespondsFailedBadNotFound()
        {
            Delete(PressureItem, On(Pressure).ReplyToRaw);
            var status = NextStatus();
            Assert.AreEqual(MonitorState.Failed, status.State);
            Assert.AreEqual(StatusCodes.BadNotFound, status.StatusCode.Code);
            Assert.AreEqual(Pressure, status.NodeId);
        }

        [TestMethod]
        public void Delete_OneOfTwoItems_OtherKeepsNotifying()
        {
            var session = StartSession();
            MonitorDefinition pressure = On(Pressure), msg = On(Msg);
            Monitor(pressure, PressureItem); Monitor(msg, MsgItem);
            NextStatus(); NextStatus();
            Delete(PressureItem, pressure.ReplyToRaw);
            NextStatus();
            CollectionAssert.AreEqual(new[] { pressure.Key }, session.Removed.ToArray());
            _coordinator.OnNotification(pressure.Key, new DataValue(new Variant(1.0)));
            _coordinator.OnNotification(msg.Key, new DataValue(new Variant("x")));
            Assert.AreEqual(MsgItem, _output.Notifications.Single().Item);
        }

        [TestMethod]
        public void Delete_WhileDisconnected_RemovedAndNotRebuilt()
        {
            var definition = On(Pressure);
            Monitor(definition, PressureItem);
            NextStatus();
            Delete(PressureItem, definition.ReplyToRaw);
            NextStatus();
            var session = StartSession();
            WaitUntil(() => !session.AppliedAll.IsEmpty, "rebuild");
            Assert.IsEmpty(session.AppliedAll.Single());
            AssertNoMoreStatuses();
        }

        [TestMethod]
        public void Delete_SessionThrowsUnexpected_StillRespondsDeleted()
        {
            var session = StartSession();
            var definition = On(Pressure);
            Monitor(definition, PressureItem);
            NextStatus();
            session.Throw = new InvalidOperationException("session is closing");
            Delete(PressureItem, definition.ReplyToRaw);
            Assert.AreEqual(MonitorState.Deleted, NextStatus().State);
        }

        [TestMethod]
        public void SessionStarted_RebuildsNonSuspendedAndSendsStatusToEach()
        {
            MonitorDefinition a = On(Pressure), b = On(Msg), c = On(Pressure, "Client2");
            _registry.Upsert(a); _registry.Upsert(b); _registry.Upsert(c);
            _registry.Suspend(c.Key);
            var session = StartSession();
            var entries = new[] { NextEntry(), NextEntry() };
            CollectionAssert.AreEquivalent(new[] { PressureItem.MonitorStatusEventTypeFullName, MsgItem.MonitorStatusEventTypeFullName },
                entries.Select(e => e.Names.MonitorStatusEventTypeFullName).ToArray());
            Assert.IsTrue(entries.All(e => e.Status.State == MonitorState.Active));
            CollectionAssert.AreEquivalent(new[] { a.Key, b.Key }, session.AppliedAll.Single().Select(d => d.Key).ToArray());
            AssertNoMoreStatuses();
        }

        [TestMethod]
        public void SessionStarted_NodeMissingFromCatalog_StatusOnItsOwnEvent()
        {
            // The item was in the previous catalog under a collision-suffixed name, then vanished from the server.
            var suffixed = new OpcUaDataItem(NodeId.Parse("ns=2;s=removed"), "ns=2;s=removed",
                OpcUaNaming.ToMessageNames(Exchange, NodeId.Parse("ns=2;s=removed")) with { Name = "Get_ns2_s_removed_2" }, typeof(double));
            _coordinator.SessionStarted(new FakeMonitorSession(), [.. Catalog, suffixed]);
            var gone = On("ns=2;s=removed");
            Monitor(gone, suffixed);
            NextStatus();
            var session = new FakeMonitorSession();
            _coordinator.SessionStarted(session, Catalog);
            var (status, names, _) = NextEntry();
            Assert.AreEqual(MonitorState.Failed, status.State);
            Assert.AreEqual(StatusCodes.BadNodeIdUnknown, status.StatusCode.Code);
            Assert.AreEqual("ns=2;s=removed", status.NodeId);
            Assert.AreEqual("Line1Plc.MonitorStatus_ns2_s_removed_2_Event", names.MonitorStatusEventTypeFullName);
            Assert.IsEmpty(session.AppliedAll.Single());
            Assert.IsNotNull(_registry.Get(gone.Key));
        }

        [TestMethod]
        public void SessionStarted_StoredNodeNeverCataloged_StatusOnDerivedEvent()
        {
            _registry.Upsert(On("ns=2;s=removed"));
            StartSession();
            var (_, names, _) = NextEntry();
            Assert.AreEqual("Line1Plc.MonitorStatus_ns2_s_removed_Event", names.MonitorStatusEventTypeFullName);
        }

        [TestMethod]
        public void SessionStarted_RemovesExpiredSuspended()
        {
            var definition = On(Pressure);
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
            var fresh = StartSession();
            Monitor(On(Pressure), PressureItem);
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
            Monitor(On(Pressure), PressureItem);
            Assert.AreEqual(MonitorState.Pending, NextStatus().State);
            Assert.IsTrue(session.Disposed);
            Assert.IsEmpty(session.Applied);
        }

        [TestMethod]
        public void ReplyToCheck_SharedMissingReplyTo_SuspendsEveryMonitorUsingIt()
        {
            var session = StartSession();
            MonitorDefinition a = On(Pressure), b = On(Msg);
            var live = On(Pressure, "Client2") with { ReplyToRaw = a.ReplyToRaw.Replace("abc", "live") };
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
        public void ReplyToCheck_DisableThrowsUnexpected_StillSuspendsWholeGroup()
        {
            var session = StartSession();
            MonitorDefinition a = On(Pressure), b = On(Msg);
            Monitor(a, PressureItem); Monitor(b, MsgItem);
            NextStatus(); NextStatus();
            session.Throw = new InvalidOperationException("session is closing");
            _output.Missing["abc"] = true;
            _coordinator.CheckReplyTos();
            WaitUntil(() => _registry.Get(a.Key)!.IsSuspended && _registry.Get(b.Key)!.IsSuspended, "suspension of both");
        }

        [TestMethod]
        public void ReplyToCheck_EveryThirtySeconds()
        {
            StartSession();
            var definition = On(Pressure);
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
            var definition = On(Pressure);
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
            var definition = On(Pressure);
            Monitor(definition, PressureItem);
            NextStatus();
            _registry.Suspend(definition.Key);
            _coordinator.OnNotification(definition.Key, new DataValue(new Variant(1.0)));
            _coordinator.OnNotification(new MonitorKey("x", Pressure), new DataValue(new Variant(1.0)));
            Assert.IsEmpty(_output.Notifications);
        }

        [TestMethod]
        public void OnNotification_Active_SendsGetEventToReplyTo()
        {
            StartSession();
            var definition = On(Pressure);
            Monitor(definition, PressureItem);
            NextStatus();
            _coordinator.OnNotification(definition.Key, new DataValue(new Variant(2.5)));
            var (item, value, replyTo) = _output.Notifications.Single();
            Assert.AreEqual(PressureItem, item);
            Assert.AreEqual(2.5, value.WrappedValue.TryGetValue(out double d) ? d : double.NaN);
            Assert.AreEqual(definition.ReplyToRaw, replyTo);
        }
    }
}
