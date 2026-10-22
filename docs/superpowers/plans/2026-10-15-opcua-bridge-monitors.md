# OPC UA Bridge Monitors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let StatePipes clients create, update and delete OPC UA monitored items through the bridge, receiving data changes as Get events, with monitors persisted and re-established on every OPC UA session.

**Architecture:** A persisted `MonitorRegistry` is the source of truth. A single-reader `MonitorCoordinator` serializes all monitor work (commands, rebuilds, suspensions) and talks to the OPC UA side through `IMonitorSession` (implemented by `OpcUaMonitorManager`, one per session) and to the broker through `IMonitorOutput` (implemented by `OpcUaBridgeService` over a dedicated notification channel). `OpcUaBridgeService` only parses/routes commands.

**Tech Stack:** .NET 10, OPCFoundation.NetStandard.Opc.Ua.Client 2.0.1, Newtonsoft.Json, RabbitMQ.Client 7, Confluent.Kafka, MSTest 4.

**Spec:** `docs/superpowers/specs/2026-10-15-opcua-bridge-monitors-design.md`

## Global Constraints

- Monitor key = (reply-to `CommandExchangeName`, `MonitorName`); one name namespace per sender — reusing a name on another node moves the monitor.
- Monitor command full name = `{namespace}.Monitor_{rest of Get name}_Command`; every full name ≤ 255 bytes.
- `MonitorStatusEvent` is sent only as a response to Monitor/Delete commands and after a rebuild on a new OPC UA session; never on the event exchange; never to suspended monitors.
- Every response (status or notification) passes the existing `SendResponse` guards (same `BrokerUri`, same `ClientCertPath`).
- Store: `MONITOR_STORE_PATH` default `Config/monitors.json` (relative to `AppContext.BaseDirectory`), `version: 1`; atomic write (temp + rename); corrupt file → `monitors.json.corrupt-{yyyyMMddTHHmmssZ}`.
- `MONITOR_SUSPENDED_EXPIRY_HOURS` default never; expiry checked on each rebuild and hourly.
- Reply-to existence check every 30 s and immediately when the notification channel is found closed.
- Validation → `Failed` + `BadInvalidArgument`: name non-empty ≤ 256 chars; `SamplingInterval ≥ 0`; `PublishingInterval > 0`; `QueueSize ≥ 1`; `DeadbandValue ≥ 0`, `≤ 100` when Percent, ignored (sent as 0) when None; enums defined.
- Not in catalog: before first discovery → `BadWaitingForInitialData`; after → `BadNodeIdUnknown`; both with empty `NodeId`, nothing stored.
- No `.Result`/`.Wait()` in new async code (CLAUDE.md Rule 1). Run `dotnet format` before each commit (Rule 3).
- Test command: `dotnet test StatePipes.Test --settings tests.runsettings --filter "FullyQualifiedName~StatePipes.Test.OpcUaBridge"`.

## Review Focus

1. **A client restarts and re-sends its Monitor commands** (new `ResponseExchangeGuid`, same `CommandExchangeName`): the monitor must update in place and notifications must go only to the new reply-to — Task 6 `Monitor_SameKeyNewReplyTo_NotificationsGoToNewReplyToOnly`.
2. **The same reply-to is used by many monitors and disappears**: all of them suspend in one check, not just the first — Task 6 `ReplyToCheck_SharedMissingReplyTo_SuspendsEveryMonitorUsingIt`.
3. **Monitor commands arriving while a rebuild is in progress** must not be lost or applied to a dead session — Task 6 `SessionStarted_ThenMonitorQueued_AppliedAfterRebuildOnNewSession`.
4. **A monitors.json written by a bridge configured for another broker/cert** must not send anywhere — Task 4 `Load_EntryFailingGuards_IsDropped`.
5. **Delete of a monitor while the OPC UA server is down** must still remove it from disk so it is not resurrected on reconnect — Task 6 `Delete_WhileDisconnected_RemovedAndNotRebuilt`.

---

### Task 1: Message types, naming and self-description

**Files:**
- Create: `StatePipes.OpcUaBridge/Messages/MonitorMessages.cs`
- Modify: `StatePipes.OpcUaBridge/OpcUaNaming.cs`, `StatePipes.OpcUaBridge/SelfDescriptionBuilder.cs`
- Test: `StatePipes.Test/OpcUaBridge/OpcUaMonitorMessagesTest.cs`

**Interfaces:**
- Produces (namespace `StatePipes.OpcUaBridge.Messages`, all `public`):
  - `enum MonitoringModeKind { Disabled, Reporting }`, `enum DataChangeTriggerKind { Status, StatusValue, StatusValueTimestamp }`, `enum DeadbandTypeKind { None, Absolute, Percent }`, `enum MonitorState { Active, Pending, Failed, Suspended, Deleted }`
  - `class DeleteMonitorCommand(string monitorName) : ICommand { string MonitorName { get; } }`
  - `class MonitorStatusEvent(string monitorName, string nodeId, MonitorState state, uint statusCode, string status, double revisedSamplingInterval, uint revisedQueueSize, double revisedPublishingInterval) : IEvent` with matching get-only properties (pattern of `HeartbeatEvent`).
- Produces: `MessageNames.MonitorCommandTypeName` / `MonitorCommandTypeFullName`; `const string MessageNames.GetPrefix = "Get_"`, `MonitorPrefix = "Monitor_"`; `OpcUaDataItem.MonitorCommandTypeFullName`.
- Produces: `SelfDescriptionBuilder` property-name constants `MonitorNameProperty`, `MonitoringModeProperty`, `DataChangeTriggerProperty`, `DeadbandTypeProperty`, `DeadbandValueProperty`, `SamplingIntervalProperty`, `QueueSizeProperty`, `PublishingIntervalProperty` (values equal to the names, e.g. `"MonitorName"`).

- [ ] **Step 1: Write failing tests**

```csharp
[TestMethod] public void ToMessageNames_MonitorCommand_SharesGetBaseName()
{
    var names = OpcUaNaming.ToMessageNames("Line1Plc", new NodeId("Machine.Motor.Speed", 2));
    Assert.AreEqual("Line1Plc.Monitor_ns2_s_Machine_Motor_Speed_Command", names.MonitorCommandTypeFullName);
}
[TestMethod] public void ToMessageNames_VeryLongIdentifier_MonitorNameFitsRoutingKey()
{   // same 40x "Segment" path as the existing long-identifier test
    Assert.IsLessThanOrEqualTo(255, $"{names.MonitorCommandTypeFullName}_999".Length);
}
[TestMethod] public void AssignUniqueNames_Collision_MonitorNameGetsSameSuffix()
{   // "A-B" vs "A_B": second node's Monitor name ends "_2_Command" exactly like its Get name
}
[TestMethod] public void SelfDescription_ConsumedLikeExplorer_MonitorAndFixedTypesEmit()
{
    // Build([temperature]) → JSON round trip → TypeSerializationJsonHelper per serialization (as the existing Explorer test)
    // emitted[temperature.MonitorCommandTypeFullName] is ICommand with properties MonitorName:string,
    //   MonitoringMode/DataChangeTrigger/DeadbandType of enum types whose names are Disabled/Reporting etc.,
    //   DeadbandValue:double, SamplingInterval:double, QueueSize:uint, PublishingInterval:double
    // emitted[typeof(DeleteMonitorCommand).FullName] is ICommand; emitted[typeof(MonitorStatusEvent).FullName] is IEvent
    // JSON of new MonitorStatusEvent("m1","ns=2;s=x",MonitorState.Active,0,"Good",100,1,1000) deserializes into the emitted type with State == Active
}
```

- [ ] **Step 2: Run tests — expect compile failure (missing members/types).**

- [ ] **Step 3: Implement**
  - `ToMessageNames`: subtract `MessageNames.MonitorPrefix.Length - MessageNames.GetPrefix.Length` (4) more from `maxNameLength`. `MonitorCommandTypeName` = `MonitorPrefix + Name[GetPrefix.Length..] + CommandSuffix` (collision suffixes live in `Name`, so they carry over).
  - `SelfDescriptionBuilder`: add `DeleteMonitorCommand`, `MonitorStatusEvent` and the four enums to `StatePipesMessageTypes`; add `BuildMonitorCommand(item)` to `Build`. Each `TypeSerialization` is self-contained, so the Monitor command serialization must also contain the three enum descriptions — copy them from `converter.CreateFromType(typeof(enum))`.

- [ ] **Step 4: Run tests — PASS, existing OpcUaBridge tests still PASS.**

- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): monitor message types and naming`

---

### Task 2: Monitor command parsing and validation

**Files:**
- Create: `StatePipes.OpcUaBridge/MonitorParameters.cs`
- Test: `StatePipes.Test/OpcUaBridge/MonitorParametersTest.cs`

**Interfaces:**
- Consumes: Task 1 enums and property-name constants.
- Produces:
  - `internal sealed record MonitorParameters(MonitoringModeKind MonitoringMode, DataChangeTriggerKind DataChangeTrigger, DeadbandTypeKind DeadbandType, double DeadbandValue, double SamplingInterval, uint QueueSize, double PublishingInterval)`
  - `internal sealed record ParsedMonitorCommand(string MonitorName, MonitorParameters? Parameters, StatusCode Status)` — `Status` Good when `Parameters` non-null.
  - `static ParsedMonitorCommand MonitorParameters.Parse(byte[] body)` — never throws; malformed JSON → `MonitorName = ""`, `BadInvalidArgument`. Enums accepted as integer or name; undefined → `BadInvalidArgument`. When `DeadbandType == None`, returned `DeadbandValue` is 0.
  - `static string? ReadMonitorName(byte[] body)` — the name alone, for not-in-catalog replies.

- [ ] **Step 1: Write failing tests** (one `[TestMethod]` per row, asserting `Status` and, when Good, every parameter):

| Test | Body change from a valid baseline | Expected |
|---|---|---|
| `Parse_Valid_ReturnsParameters` | baseline `{MonitorName:"m1", MonitoringMode:1, DataChangeTrigger:"StatusValue", DeadbandType:1, DeadbandValue:0.5, SamplingInterval:100, QueueSize:2, PublishingInterval:1000}` | Good, Reporting/StatusValue/Absolute/0.5/100/2/1000 |
| `Parse_EmptyOrLongName_Invalid` | `""`, 257 chars | Bad; 256 chars Good |
| `Parse_NegativeSampling_Invalid` | `-1` | Bad; `0` Good |
| `Parse_NonPositivePublishing_Invalid` | `0` | Bad |
| `Parse_ZeroQueue_Invalid` | `0` | Bad |
| `Parse_PercentOver100_Invalid` | Percent, 100.1 | Bad; Percent 100 Good |
| `Parse_NegativeDeadband_Invalid` | Absolute, -0.1 | Bad |
| `Parse_NoneDeadband_ValueIgnored` | None, -5 | Good, DeadbandValue 0 |
| `Parse_UndefinedEnum_Invalid` | `MonitoringMode: 7`, `DeadbandType:"Bogus"` | Bad |
| `Parse_Garbage_InvalidNoThrow` | bytes `{not json` | Bad, MonitorName "" |

Bad = `StatusCodes.BadInvalidArgument`, `Parameters == null`.

- [ ] **Step 2: Run — FAIL (type missing).**
- [ ] **Step 3: Implement with `JObject.Parse` inside try/catch.**
- [ ] **Step 4: Run — PASS.**
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): monitor command parsing and validation`

---

### Task 3: Monitor definitions and the store file

**Files:**
- Create: `StatePipes.OpcUaBridge/MonitorDefinition.cs`, `StatePipes.OpcUaBridge/MonitorStore.cs`
- Test: `StatePipes.Test/OpcUaBridge/MonitorStoreTest.cs`

**Interfaces:**
- Consumes: `MonitorParameters` (Task 2).
- Produces:
  - `internal readonly record struct MonitorKey(string SenderCommandExchange, string MonitorName)`
  - `internal sealed record MonitorDefinition(MonitorKey Key, string NodeIdText, MonitorParameters Parameters, string ReplyToRaw, DateTime? SuspendedSince)` with `BusConfig? ReplyTo` (parsed once from `ReplyToRaw` via `JsonUtility.GetObjectForJsonString<BusConfig>`; null if unparseable).
  - `internal sealed class MonitorStore(string path)`: `IReadOnlyList<MonitorDefinition> Load()`; `bool Save(IReadOnlyList<MonitorDefinition> monitors)`.
- File format: exactly the spec's JSON (camelCase names, enums as names, `replyTo` = raw header string, `suspendedSince` ISO-8601 UTC or null).

- [ ] **Step 1: Write failing tests** (temp directory per test, deleted in cleanup):
  - `SaveThenLoad_RoundTripsEveryField` — two definitions, one suspended; `Load()` equals what was saved.
  - `Save_WritesSpecFormat` — file JSON has `version == 1`, `monitors[0].deadbandType == "Absolute"`, `replyTo` is a string.
  - `Load_MissingFile_Empty` — no file → empty, no file created.
  - `Load_CorruptFile_RenamedAndEmpty` — write `{oops`; `Load()` empty; directory contains exactly one `monitors.json.corrupt-*` with the original bytes; no `monitors.json`.
  - `Load_UnknownVersion_TreatedAsCorrupt` — `{"version":2,"monitors":[]}` → renamed, empty.
  - `Save_ReadOnlyDirectory_ReturnsFalseAndKeepsOldFile` — make the existing file read-only (`File.SetAttributes`), `Save` returns false, old content intact.
  - `Save_NeverLeavesTempFile` — after `Save`, only `monitors.json` exists.
- [ ] **Step 2: Run — FAIL.**
- [ ] **Step 3: Implement.** Save: serialize → write `path + ".tmp"` → `File.Move(tmp, path, overwrite: true)`; on exception log error, delete tmp, return false. Create the directory if missing.
- [ ] **Step 4: Run — PASS.**
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): monitor store file`

---

### Task 4: Monitor registry

**Files:**
- Create: `StatePipes.OpcUaBridge/MonitorRegistry.cs`, `StatePipes.OpcUaBridge/ReplyToGuard.cs`
- Modify: `StatePipes.OpcUaBridge/OpcUaBridgeService.cs` (`SendResponse` uses `ReplyToGuard`)
- Test: `StatePipes.Test/OpcUaBridge/MonitorRegistryTest.cs`

**Interfaces:**
- Consumes: Task 3.
- Produces:
  - `internal static class ReplyToGuard { static bool Allows(BusConfig own, BusConfig replyTo, out string reason); }` — the two checks currently inline in `SendResponse`, with the same log text as `reason`.
  - `internal sealed class MonitorRegistry` (thread-safe; every mutation calls `store.Save(GetAll())`):
    - `static MonitorRegistry Load(MonitorStore store, BusConfig own, TimeSpan? suspendedExpiry, TimeProvider time)` — drops entries whose `ReplyTo` is null or fails `ReplyToGuard` (warning logged), then `RemoveExpired()`.
    - `MonitorDefinition? Get(MonitorKey key)`
    - `MonitorDefinition? Upsert(MonitorDefinition definition)` — stores it with `SuspendedSince = null`; returns the previous definition or null.
    - `bool Remove(MonitorKey key)`
    - `void Suspend(MonitorKey key)` — sets `SuspendedSince = time.GetUtcNow().UtcDateTime` if not already suspended.
    - `IReadOnlyList<MonitorKey> RemoveExpired()`
    - `IReadOnlyList<MonitorDefinition> GetAll()`

- [ ] **Step 1: Write failing tests** (use `Microsoft.Extensions.Time.Testing.FakeTimeProvider` — add package `Microsoft.Extensions.TimeProvider.Testing` 10.0.0 to the test project — and a temp-dir store):
  - `Upsert_NewKey_ReturnsNullAndPersists` — new registry loaded from the same file sees it.
  - `Upsert_SameKeyOtherNode_ReplacesDefinition` — previous returned; `Get` has new NodeId; `GetAll().Count == 1`.
  - `Upsert_SameNameOtherSender_SeparateMonitors`.
  - `Upsert_Suspended_ClearsSuspension`.
  - `Suspend_ThenReload_SuspensionPersisted`.
  - `RemoveExpired_PastExpiry_RemovesOnlyExpiredSuspended` — expiry 2 h; suspend A; advance 3 h; suspend B; `RemoveExpired()` returns [A]; active C untouched.
  - `RemoveExpired_NoExpiryConfigured_KeepsAll`.
  - `Load_EntryFailingGuards_IsDropped` — file holds one entry with a different `BrokerUri` and one with a different `ClientCertPath`, one valid → only the valid one loaded.
  - `Mutation_SaveFails_RegistryStillUpdated` — store path in a read-only file; `Upsert` succeeds in memory.
- [ ] **Step 2: Run — FAIL.**
- [ ] **Step 3: Implement** (single `lock`; definitions in a `Dictionary<MonitorKey, MonitorDefinition>`).
- [ ] **Step 4: Run — PASS; existing bridge tests PASS.**
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): monitor registry`

---

### Task 5: OPC UA monitor manager and in-process test server

**Files:**
- Create: `OpcUaServer/PlcServerHost.cs`; `StatePipes.OpcUaBridge/IMonitorSession.cs`; `StatePipes.OpcUaBridge/OpcUaMonitorManager.cs`; `StatePipes.Test/OpcUaBridge/PlcServerFixture.cs`
- Modify: `OpcUaServer/Worker.cs` (use `PlcServerHost`); `StatePipes.OpcUaBridge/OpcUaClient.cs` (`CreateMonitorManager`); `StatePipes.Test/StatePipes.Test.csproj` (reference `..\OpcUaServer\OpcUaServer.csproj`); `StatePipes.Test/TestCategories.cs` (`Integration = "Integration"`)
- Test: `StatePipes.Test/OpcUaBridge/OpcUaMonitorManagerTest.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces:
  - `public static class PlcServerHost { static Task<PlcServer> StartAsync(string endpointUrl, string pkiRoot, ITelemetryContext telemetry, CancellationToken ct); }` — the existing `BuildServerConfiguration` body with `BaseAddresses = [endpointUrl]` and PKI under `pkiRoot`. `Worker` calls it with its current URL and `%LocalApplicationData%/PLCSimulator/pki`.
  - `internal sealed record MonitorApplyResult(MonitorState State, StatusCode StatusCode, double RevisedSamplingInterval, uint RevisedQueueSize, double RevisedPublishingInterval)` with `static MonitorApplyResult Pending` (`BadNotConnected`, zeros) and `static MonitorApplyResult Failed(StatusCode)`.
  - `internal interface IMonitorSession : IAsyncDisposable`
    - `Task<MonitorApplyResult> ApplyAsync(MonitorDefinition definition, CancellationToken ct)`
    - `Task<IReadOnlyDictionary<MonitorKey, MonitorApplyResult>> ApplyAllAsync(IReadOnlyList<MonitorDefinition> definitions, CancellationToken ct)` — one `CreateMonitoredItems` per subscription.
    - `Task RemoveAsync(MonitorKey key, CancellationToken ct)`
    - `Task DisableAsync(MonitorKey key, CancellationToken ct)`
  - `IMonitorSession OpcUaClient.CreateMonitorManager(Action<MonitorKey, DataValue> onNotification)` — throws `InvalidOperationException` when not connected.
  - Mapping: `MonitoringMode` → `Opc.Ua.MonitoringMode`; always a `DataChangeFilter { Trigger, DeadbandType, DeadbandValue }`; `TimestampsToReturn.Both`; `DiscardOldest = true`. Subscriptions keyed by `PublishingInterval`; empty ones deleted. A call-level `ServiceResultException` → every affected monitor `Pending`.

- [ ] **Step 1: Extract `PlcServerHost`; run OpcUaServer manually (`dotnet run --project OpcUaServer`) — "OPC UA Server started" logged.**
- [ ] **Step 2: Write `PlcServerFixture`** — starts `PlcServerHost` on `opc.tcp://localhost:48401/PLCSimulator` with PKI in a temp dir, exposes `NodeManager`, `RestartAsync()` (stop + start on the same URL), and an `OpcUaClient` with `UseSecurity=false`, `AutoAcceptUntrustedCertificates=true`, `NodeFilter=null`. Pressure node is `ns=2;s=pressure` (verify index from `NamespaceUris` at startup).
- [ ] **Step 3: Write failing tests** (`[TestCategory(TestCategories.Integration)]`; values pushed with `NodeManager.UpdatePressure`; wait for notifications with a 5 s timeout helper):
  - `Apply_Reporting_ActiveWithRevisedValuesAndNotifies` — Active, Good, revised publishing interval > 0; pressure 10 → 11 yields a notification with value 11.
  - `Apply_AbsoluteDeadband_SuppressesSmallChanges` — deadband 1.0; 10 → 10.5 → 12: notifications for 12 only (after the initial).
  - `Apply_PercentOnNonAnalogNode_FailedFilterNotAllowed` — `State == Failed`, `StatusCode == BadFilterNotAllowed`.
  - `Apply_UnknownNode_FailedNodeIdUnknown`.
  - `Apply_SameKeyNewPublishingInterval_MovesSubscriptionAndKeepsNotifying` — 1000 → 250; revised ≈ 250; a further change still notifies once.
  - `Apply_SameKeyNewNode_NotificationsOnlyFromNewNode`.
  - `Remove_StopsNotifications` and `Disable_StopsNotifications`.
  - `ApplyAll_GroupsByPublishingInterval` — 3 definitions over 2 intervals → all Active.
- [ ] **Step 4: Run — FAIL.**
- [ ] **Step 5: Implement `OpcUaMonitorManager`.** Notifications use `MonitoredItem.Notification` → `onNotification(key, value)` for each `MonitoredItemNotification`.
- [ ] **Step 6: Run — PASS.**
- [ ] **Step 7: `dotnet format`, commit** `feat(opcua-bridge): OPC UA monitor manager with in-process test server`

---

### Task 6: Monitor coordinator

**Files:**
- Create: `StatePipes.OpcUaBridge/IMonitorOutput.cs`, `StatePipes.OpcUaBridge/MonitorCoordinator.cs`
- Test: `StatePipes.Test/OpcUaBridge/MonitorCoordinatorTest.cs` (with `FakeMonitorSession` and `FakeMonitorOutput` in the same file)

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces:
  - `internal interface IMonitorOutput`
    - `void SendStatus(MonitorStatusEvent status, string replyToRaw)`
    - `void SendNotification(OpcUaDataItem item, DataValue value, string replyToRaw)`
    - `Task<bool> ReplyToExistsAsync(BusConfig replyTo, CancellationToken ct)`
  - `internal sealed class MonitorCoordinator(MonitorRegistry registry, IMonitorOutput output, TimeProvider time)`
    - `Task RunAsync(CancellationToken ct)` — single reader over an unbounded `Channel<MonitorWork>`; also drives a 30 s reply-to check and hourly `RemoveExpired` via `time`.
    - `void Monitor(MonitorKey key, OpcUaDataItem item, MonitorParameters parameters, string replyToRaw)`
    - `void Delete(MonitorKey key, string replyToRaw)`
    - `void SessionStarted(IMonitorSession session, IReadOnlyList<OpcUaDataItem> catalog)` — disposes any previous session.
    - `void SessionLost()`
    - `void CheckReplyTos()` — enqueue an immediate check.
    - `void OnNotification(MonitorKey key, DataValue value)` — not queued; looks up the definition and current catalog item by NodeId text; sends via `output` unless suspended or unknown.

- [ ] **Step 1: Write failing tests** (fakes record calls; `FakeMonitorSession` returns configurable results per key; drive with `RunAsync` on a background task and a helper that waits until the work channel is drained):
  - `Monitor_Connected_UpsertsAppliesAndRespondsActive` — status has name, NodeId text, revised values.
  - `Monitor_Disconnected_StoredPendingResponse`.
  - `Monitor_SessionFails_StoredFailedResponseWithCode` — `BadFilterNotAllowed` kept in registry.
  - `Monitor_SameKeyNewReplyTo_NotificationsGoToNewReplyToOnly`.
  - `Monitor_SuspendedKey_Reclaimed` — `SuspendedSince` cleared, session `ApplyAsync` called.
  - `Delete_Existing_RemovedRespondsDeleted`; `Delete_Unknown_RespondsFailedBadNotFound`.
  - `Delete_WhileDisconnected_RemovedAndNotRebuilt` — delete, then `SessionStarted`: `ApplyAllAsync` never sees the key.
  - `SessionStarted_RebuildsNonSuspendedAndSendsStatusToEach` — 2 active + 1 suspended: `ApplyAllAsync` gets 2; 2 statuses; suspended gets none and is not applied.
  - `SessionStarted_NodeMissingFromCatalog_FailedBadNodeIdUnknown`.
  - `SessionStarted_RemovesExpiredSuspended`.
  - `SessionStarted_ThenMonitorQueued_AppliedAfterRebuildOnNewSession`.
  - `SessionLost_ThenMonitor_Pending`.
  - `ReplyToCheck_SharedMissingReplyTo_SuspendsEveryMonitorUsingIt` — 2 monitors share a missing reply-to, 1 has a live one: both disabled and suspended; third untouched; exists-check called once per distinct reply-to.
  - `ReplyToCheck_EveryThirtySeconds` — advance `FakeTimeProvider` 30 s → check runs.
  - `Expiry_Hourly_RemovesExpiredWithoutRebuild` — expiry 1 h; suspend; advance 2 h → removed from registry and file.
  - `OnNotification_SuspendedOrUnknown_NotSent`; `OnNotification_Active_SendsGetEventToReplyTo`.
- [ ] **Step 2: Run — FAIL.**
- [ ] **Step 3: Implement.** `MonitorWork` is a private abstract record with one subtype per entry point. Every status send goes through `ReplyToGuard` first (log and drop on failure).
- [ ] **Step 4: Run — PASS.**
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): monitor coordinator`

---

### Task 7: Broker bus existence check

**Files:**
- Modify: `StatePipes.BrokerProxy/ISimpleConnectionChannel.cs`, `StatePipes.BrokerProxy/RabbitMqConnectionChannel.cs`, `StatePipes.BrokerProxy/KafkaConnectionChannel.cs`

**Interfaces:**
- Produces: `Task<bool> ISimpleConnectionChannel.BusExistsAsync(string exchangeName, CancellationToken ct)`
  - RabbitMQ: new temporary `IChannel` from the current connection; `ExchangeDeclarePassiveAsync`; `OperationInterruptedException` with `ShutdownReason.ReplyCode == 404` → false; temporary channel disposed; no connection → true (unknown is not missing).
  - Kafka: `AdminClient.GetMetadata(KafkaTopicNamer.ToTopic(exchangeName), 5 s)` inside `Task.Run`; topic error `UnknownTopicOrPart` → false; any other failure → true.

- [ ] **Step 1: Implement both.**
- [ ] **Step 2: `dotnet build` — whole solution builds (BrokerProxy and the bridge both compile these files).**
- [ ] **Step 3: Verified manually in Task 9's checklist (no broker in the unit test run).**
- [ ] **Step 4: `dotnet format`, commit** `feat(brokerproxy): bus existence check on simple channels`

---

### Task 8: Wire the bridge

**Files:**
- Modify: `StatePipes.OpcUaBridge/OpcUaBridgeService.cs`, `StatePipes.OpcUaBridge/ServiceWorker.cs`, `StatePipes.OpcUaBridge/BridgeSettings.cs`
- Test: `StatePipes.Test/OpcUaBridge/OpcUaBridgeMonitorTest.cs` (with `FakeConnectionChannel : ISimpleConnectionChannel` recording sends and exposing the captured command consumer)

**Interfaces:**
- Consumes: Tasks 1–7.
- Produces:
  - `BridgeSettings.MonitorStorePath` (string, default `Path.Combine(AppContext.BaseDirectory, "Config", "monitors.json")`), `BridgeSettings.MonitorSuspendedExpiry` (`TimeSpan?`, null default) from `MONITOR_STORE_PATH`, `MONITOR_SUSPENDED_EXPIRY_HOURS`.
  - `OpcUaBridgeService(BusConfig busConfig, OpcUaClient opcUa, MonitorCoordinator? monitors = null, Func<BusConfig, Action<ISimpleConnectionChannel>?, CancellationToken, ISimpleConnectionChannel>? channelFactory = null)` — default factory `SimpleConnectionChannelFactory.Create(config, null, configure, ct)`. Implements `IMonitorOutput`.
  - Catalog gains `ByMonitorCommand` and `bool Discovered` (false until the first `SetCatalog`).
  - `internal static Task ServiceWorker.MaintainOpcUaSessionAsync(BridgeSettings, OpcUaClient, OpcUaBridgeService, MonitorCoordinator, CancellationToken)` — after `SetCatalog`, `monitors.SessionStarted(opcUa.CreateMonitorManager(monitors.OnNotification), items)`; after the session ends, `monitors.SessionLost()`.
- Behaviour:
  - Main channel unchanged; a second channel from the same factory with no buses configured carries notifications and monitor statuses. If found closed on send → `monitors.CheckReplyTos()`.
  - `ConsumeCommand` routing: Get (unchanged); `ByMonitorCommand` hit → `MonitorParameters.Parse` → invalid → `Failed` status directly, else `monitors.Monitor(...)`; routing key starting `{namespace}.Monitor_` and ending `_Command` but not in catalog → `Failed` + (`Discovered ? BadNodeIdUnknown : BadWaitingForInitialData`) with `ReadMonitorName`; `DeleteMonitorCommand` full name → `monitors.Delete(...)`. Key's `SenderCommandExchange` = parsed reply-to's `CommandExchangeName`. Guards apply before anything is queued.

- [ ] **Step 1: Write failing tests** (unit; fake channel; real `MonitorCoordinator` with `FakeMonitorSession` from Task 6):
  - `MonitorCommand_BeforeDiscovery_FailedWaitingForInitialData` — one status sent to the reply-to's `ResponseExchangeName`, routing key `MonitorStatusEvent` full name, `NodeId == ""`.
  - `MonitorCommand_UnknownAfterDiscovery_FailedNodeIdUnknown`.
  - `MonitorCommand_Invalid_FailedInvalidArgumentNothingStored`.
  - `MonitorCommand_Valid_ReachesCoordinatorAndRespondsOnNotificationChannel`.
  - `MonitorCommand_OtherBroker_DroppedNoResponse`.
  - `DeleteMonitorCommand_BeforeDiscovery_Processed`.
  - `GetCommand_StillAnsweredOnMainChannel`.
  - `NotificationChannelClosed_TriggersReplyToCheck`.
- [ ] **Step 2: Run — FAIL.**
- [ ] **Step 3: Implement; `ServiceWorker.ExecuteAsync` loads `MonitorRegistry.Load(new MonitorStore(settings.MonitorStorePath), settings.BusConfig, settings.MonitorSuspendedExpiry, TimeProvider.System)`, creates the coordinator and adds `monitors.RunAsync` to the `Task.WhenAll`.**
- [ ] **Step 4: Run — PASS; full OpcUaBridge filter PASS.**
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): route monitor commands and send notifications`

---

### Task 9: End-to-end recovery tests, docs and manual checklist

**Files:**
- Test: `StatePipes.Test/OpcUaBridge/OpcUaBridgeMonitorRecoveryTest.cs` (`[TestCategory(TestCategories.Integration)]`, uses `PlcServerFixture` + `FakeConnectionChannel`)
- Modify: `StatePipes.OpcUaBridge/README.md`

**Interfaces:**
- Consumes: everything above; runs `ServiceWorker.MaintainOpcUaSessionAsync` with `ReconnectDelay = 1 s`.

- [ ] **Step 1: Write tests:**
  - `Monitor_EndToEnd_GetEventsOnChange` — send Monitor command via the fake channel consumer; status Active; pressure change → Get event body (deserialized like the existing Explorer test) on the reply-to.
  - `ServerReboot_MonitorsRebuiltWithStatusAndNotifications` — `fixture.RestartAsync()`; within 30 s a second Active status arrives; a following change notifies.
  - `BridgeRestart_MonitorsRestoredFromFile` — tear down bridge + coordinator, build new ones on the same store path; status Active arrives without re-sending; change notifies.
  - `MissingReplyTo_Suspended_GetsStillServed_ReclaimRestores` — fake channel `BusExistsAsync` returns false; after `CheckReplyTos` no notifications and file shows `suspendedSince`; a Get still answers; re-send Monitor → Active and notifications resume.
- [ ] **Step 2: Run — PASS (fix any defects in the owning task's code).**
- [ ] **Step 3: README** — Monitor/Delete commands and `MonitorStatusEvent` fields; status codes table (`BadInvalidArgument`, `BadWaitingForInitialData`, `BadNodeIdUnknown`, `BadNotFound`, `BadFilterNotAllowed`, `BadNotConnected` for Pending); `MONITOR_STORE_PATH`, `MONITOR_SUSPENDED_EXPIRY_HOURS`; `Config/` must be a mounted volume; Kafka requires `auto.create.topics.enable=false` for suspension.
- [ ] **Step 4: Full run** `dotnet test --settings tests.runsettings` — all tests PASS; `dotnet format --verify-no-changes` clean.
- [ ] **Step 5: Manual checklist (record results in the PR description):**
  - RabbitMQ (`amqps://amqp09-broker/Production`) + `opc-plc`: monitor via Explorer; stop Explorer → within 30 s log shows suspension, Gets from a second client still answered, no channel churn in bridge log.
  - Kafka (`ssl://kafka-broker:9093`, `auto.create.topics.enable=false`): same; suspension observed.
  - Restart `opc-plc` container → monitors return and Explorer receives status + values.
  - Restart bridge container with `Config/` mounted → monitors return.
- [ ] **Step 6: Commit** `test(opcua-bridge): monitor recovery tests and docs`
