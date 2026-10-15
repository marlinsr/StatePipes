# OPC UA Bridge Monitors — Revision 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Identify monitors by (sender, data item) instead of a monitor name, and replace the generic Delete command and status event with per-item `Delete_…_Command` and `MonitorStatus_…_Event` in each item's namespace.

**Architecture:** Same components as revision 1. `MonitorKey` becomes `(SenderCommandExchange, NodeIdText)`; the coordinator sends statuses through `IMonitorOutput.SendStatus(MonitorStatus, MessageNames, replyToRaw)` so the bridge can route each to the item's own status event; the bridge routes per-item Delete commands and derives status event names from routing keys for items not in the catalog.

**Tech Stack:** .NET 10, OPCFoundation.NetStandard.Opc.Ua.Client 2.0.1, Newtonsoft.Json, MSTest 4.

**Spec:** `docs/superpowers/specs/2026-10-15-opcua-bridge-monitors-design.md` (revision 2)

## Global Constraints

- Names: `Monitor_`, `Delete_` + `_Command`; `MonitorStatus_` + `_Event`; all sharing the Get name's base and collision suffix; budget reduced by 8 (`MonitorStatus_`+`_Event` vs `Get_`+`_Command`); every full name ≤ 255 bytes.
- `MonitorName` removed everywhere (commands, status, store, key, validation, README).
- `DeleteMonitorCommand` and `MonitorStatusEvent` classes removed; enums stay.
- Store `version: 2`, entries keyed by `senderCommandExchange` + `nodeId`; any other version is set aside as corrupt.
- Delete body is ignored (empty expected).
- Not in catalog (Monitor or Delete): reply on `{namespace}.MonitorStatus_{base}_Event` derived from the routing key; `BadWaitingForInitialData` before first discovery, else `BadNodeIdUnknown`; empty `NodeId`; nothing stored.
- Every `dotnet` call needs `MsBuildSDKsPath="C:\Program Files\dotnet\sdk\10.0.402\Sdks"`. Run `dotnet format` on touched projects before each commit.
- Test command: `dotnet test StatePipes.Test --settings tests.runsettings --filter "FullyQualifiedName~StatePipes.Test.OpcUaBridge"`.

## Review Focus

1. **A client monitors two items, then deletes one**: the other keeps notifying — Task 3 `Delete_OneOfTwoItems_OtherKeepsNotifying`.
2. **A Delete arrives for an item with a collision-suffixed name (`…_2`)**: routed to the right item and answered on `MonitorStatus_…_2_Event` — Task 4 `DeleteCommand_CollisionSuffixedItem_AnsweredOnItsOwnStatusEvent`.
3. **A rebuild finds a stored item missing from the new catalog**: its status still goes out on that item's status event name — Task 3 `SessionStarted_NodeMissingFromCatalog_StatusOnItsOwnEvent`.
4. **A revision-1 `monitors.json`** is set aside, not half-read — Task 2 `Load_VersionOne_TreatedAsCorrupt`.
5. **Two senders monitor the same item**: independent monitors, each notified at its own reply-to — Task 3 `SameItemTwoSenders_EachNotifiedAtOwnReplyTo`.

---

### Task 1: Per-item names and self-description

**Files:** Modify `OpcUaNaming.cs` (`MessageNames`), `OpcUaDataItem.cs`, `SelfDescriptionBuilder.cs`, `Messages/MonitorMessages.cs`; Test `OpcUaMonitorMessagesTest.cs`.

**Interfaces — Produces:**
- `MessageNames.DeletePrefix = "Delete_"`, `MonitorStatusPrefix = "MonitorStatus_"`; `DeleteCommandTypeName/FullName`, `MonitorStatusEventTypeName/FullName`; `OpcUaDataItem.DeleteCommandTypeFullName`, `MonitorStatusEventTypeFullName`.
- `static bool MessageNames.TryGetMonitorStatusEventFor(string commandFullName, string @namespace, out string statusEventFullName)` — for `{ns}.Monitor_X_Command` or `{ns}.Delete_X_Command` returns `{ns}.MonitorStatus_X_Event`; false for anything else.
- `SelfDescriptionBuilder` property constants: remove `MonitorNameProperty`; add `StateProperty = "State"`, `RevisedSamplingIntervalProperty`, `RevisedQueueSizeProperty`, `RevisedPublishingIntervalProperty` (values = names).

- [ ] **Step 1: Failing tests** — `ToMessageNames_PerItemNames_ShareGetBase` (`Line1Plc.Delete_ns2_s_Machine_Motor_Speed_Command`, `Line1Plc.MonitorStatus_ns2_s_Machine_Motor_Speed_Event`); long-identifier and long-exchange tests assert `MonitorStatusEventTypeFullName + "_999"` ≤ 255; collision test asserts `_2` on Delete and MonitorStatus; `TryGetMonitorStatusEventFor_MonitorAndDelete_DerivesStatusName` (+ false for a Get command and for another namespace); Explorer test: Monitor has no `MonitorName`, Delete is `ICommand` with no properties, MonitorStatus is `IEvent` with `State` of enum type (names Active…Deleted) and the six other properties; no top-level `DeleteMonitorCommand`/`MonitorStatusEvent`.
- [ ] **Step 2: Run — FAIL.**
- [ ] **Step 3: Implement**; delete the two classes from `MonitorMessages.cs` and from `StatePipesMessageTypes`; budget `- 8` with a comment naming the longest pair.
- [ ] **Step 4: Run — PASS** (other tasks' tests may not compile yet: do Tasks 1–4 as one build-green sequence, committing per task once the solution builds).
- [ ] **Step 5: Commit** `feat(opcua-bridge): per-item Delete and MonitorStatus messages`

### Task 2: Parameters, key and store

**Files:** Modify `MonitorParameters.cs`, `MonitorDefinition.cs`, `MonitorStore.cs`, `MonitorRegistry.cs`; Tests `MonitorParametersTest.cs`, `MonitorStoreTest.cs`, `MonitorRegistryTest.cs`.

**Interfaces — Produces:**
- `ParsedMonitorCommand(MonitorParameters? Parameters, StatusCode Status)`; `MonitorParameters.Parse(byte[])` without name rules; `ReadMonitorName` removed.
- `MonitorKey(string SenderCommandExchange, string NodeIdText)`; `MonitorDefinition(MonitorKey Key, MonitorParameters Parameters, string ReplyToRaw, DateTime? SuspendedSince)` with `string NodeIdText => Key.NodeIdText`.
- Store `version: 2`, no `monitorName`.

- [ ] **Step 1: Failing tests** — remove name tests; `Parse_NoName_Valid`; store `Save_WritesSpecFormat` asserts `version == 2` and no `monitorName` property; `Load_VersionOne_TreatedAsCorrupt`; registry `Upsert_SameItemSameSender_Replaces`, `Upsert_SameItemOtherSender_SeparateMonitors` (replacing the name/move tests). Test helper `Definition(nodeId, …, sender)` keyed by node.
- [ ] **Step 2: Run — FAIL.** **Step 3: Implement.** **Step 4: Run — PASS.** **Step 5: Commit** `refactor(opcua-bridge): monitors keyed by sender and data item`

### Task 3: Coordinator and manager

**Files:** Modify `IMonitorOutput.cs`, `MonitorCoordinator.cs`, `OpcUaMonitorManager.cs`, `OpcUaClient.cs`; Tests `MonitorCoordinatorTest.cs`, `OpcUaMonitorManagerTest.cs`.

**Interfaces — Produces:**
- `internal sealed record MonitorStatus(string NodeId, MonitorState State, StatusCode StatusCode, double RevisedSamplingInterval, uint RevisedQueueSize, double RevisedPublishingInterval)`.
- `IMonitorOutput.SendStatus(MonitorStatus status, MessageNames names, string replyToRaw)` (replaces the `MonitorStatusEvent` overload).
- `MonitorCoordinator(MonitorRegistry, IMonitorOutput, TimeProvider, string exchangeName)`; `Monitor(string senderCommandExchange, OpcUaDataItem item, MonitorParameters, string replyToRaw)`; `Delete(string senderCommandExchange, OpcUaDataItem item, string replyToRaw)`; `OnNotification(MonitorKey, DataValue)`.
- Item names for statuses: current catalog → last catalog that held the node → `OpcUaNaming.ToMessageNames(exchangeName, NodeId.Parse(text))`.
- Manager callback back to `Action<MonitorKey, DataValue>` (the key now carries the node; the revision-1 previous-node check and `ItemHandle` are removed).

- [ ] **Step 1: Failing tests** — adapt every coordinator test to the new signatures (assert `names.MonitorStatusEventTypeFullName` per status); remove the move and previous-node tests; add `Delete_OneOfTwoItems_OtherKeepsNotifying`, `SessionStarted_NodeMissingFromCatalog_StatusOnItsOwnEvent`, `SameItemTwoSenders_EachNotifiedAtOwnReplyTo`; manager tests drop name-based keys and `Apply_SameKeyNewNode_…`.
- [ ] **Step 2: Run — FAIL.** **Step 3: Implement.** **Step 4: Run — PASS.** **Step 5: Commit** `refactor(opcua-bridge): coordinator statuses per data item`

### Task 4: Bridge routing

**Files:** Modify `OpcUaBridgeService.cs`; Test `OpcUaBridgeMonitorTest.cs`.

**Behaviour:** catalog gains `ByDeleteCommand`; `SendStatus` serializes `MonitorStatus` as a JObject with the spec's property names (`State` as its enum name) on `names.MonitorStatusEventTypeFullName`; Delete commands route by catalog to `Monitors.Delete`; Monitor or Delete not in catalog → `TryGetMonitorStatusEventFor` → `Failed` status on the derived name; generic `DeleteMonitorCommand` routing and the empty-name rule removed.

- [ ] **Step 1: Failing tests** — adapt existing ones to per-item names; add `DeleteCommand_BeforeDiscovery_FailedWaitingForInitialData`, `DeleteCommand_NoMonitor_FailedNotFound`, `DeleteCommand_CollisionSuffixedItem_AnsweredOnItsOwnStatusEvent`, `StatusEvent_BodyHasSpecProperties` (no `MonitorName`, `State` is `"Active"`).
- [ ] **Step 2: Run — FAIL.** **Step 3: Implement.** **Step 4: Run — PASS.** **Step 5: Commit** `feat(opcua-bridge): route per-item Delete commands`

### Task 5: Recovery tests, README, size

**Files:** Modify `OpcUaBridgeMonitorRecoveryTest.cs`, `README.md`.

- [ ] **Step 1:** Adapt recovery tests to per-item names (no `MonitorName`; statuses read from `MonitorStatus_…_Event`); add `Delete_EndToEnd_StopsNotifications`. Run — PASS.
- [ ] **Step 2:** Measure self-description bytes per variable (temporary probe test, deleted after) and update the README's size and capacity figures.
- [ ] **Step 3:** README: per-item naming table gains Delete and MonitorStatus columns; Monitor body without `MonitorName`; Delete has an empty body; status section names `MonitorStatus_…_Event` and drops `MonitorName`; identity bullet says one monitor per item per sender; remove `DeleteMonitorCommand` text.
- [ ] **Step 4:** Full suite `dotnet test StatePipes.sln --settings tests.runsettings` — PASS; `dotnet format --verify-no-changes` clean on touched projects.
- [ ] **Step 5: Commit** `docs(opcua-bridge): per-item monitor messages in README`
