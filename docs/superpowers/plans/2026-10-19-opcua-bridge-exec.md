# OPC UA Bridge Exec Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose a per-(object, method) `Exec_…_Command` for every executable OPC UA method, call it, and answer the sender with `ExecResult_…_Event`; serve every Get, Set, Exec, Monitor and Delete through one bridge-wide ordered queue.

**Architecture:** Discovery also browses Method nodes and reads their arguments into `OpcUaMethodItem`s. A new `CommandExecutor` replaces `SetWriter` and the Get loop: it takes runs of one kind from the head of a single queue (Gets → one Read, Sets → one Write, everything else alone) and answers in arrival order. Exec encodes inputs with `ExecArgumentEncoder`, calls through `OpcUaClient.CallAsync` without a timeout, and builds the answer with `ExecResultBody`. Monitor and Delete become awaitable on `MonitorCoordinator`, so they take their turn too.

**Tech Stack:** .NET 10, OPCFoundation.NetStandard.Opc.Ua.Client/Server 2.0.1, Newtonsoft.Json, MSTest 4.

**Spec:** `docs/superpowers/specs/2026-10-19-opcua-bridge-exec-design.md`

## Global Constraints

- Names: `Exec_` + base + `_Command`, `ExecResult_` + base + `_Event`. Base is `ns{ns}_{letter}_{id}` of the method, or `{object base}__{method base}` for **every** pair whose method node is reached from more than one object. `MessageNames.LongestNameExtra` stays 8.
- Method `MessageNames` are stored like item ones, `Name = "Get_" + base`, so `Base` and the collision suffix work unchanged; a method's Get names are never used.
- `MessageNames.FromExecCommandFullName` is separate from `FromCommandFullName` and `FromSetCommandFullName`.
- ExecResult body: `ObjectId` (string), `MethodId` (string), `StatusCode` (uint, `.Code`), `Status` (string, `StatusCode.ToString()`), then one property per output, present only when the status is not Bad and the value is not null. New constants `SelfDescriptionBuilder.ObjectIdProperty = "ObjectId"`, `MethodIdProperty = "MethodId"`.
- Exec status codes: `BadWaitingForInitialData`, `BadMethodInvalid`, `BadArgumentsMissing`, `BadTooManyArguments`, `BadTypeMismatch`, `BadOutOfRange`, `BadNotConnected`, `BadCommunicationError` (Call threw a non-`ServiceResultException`); server codes passed through.
- `BadInvalidArgument` Status text: `"{status}: {name} ({code}), {name} ({code})"` for each input whose `InputArgumentResults[i]` is Bad.
- Methods are excluded when `OpcUaClient.IsServerOwnNamespace(methodId.NamespaceIndex, …)` is true, **regardless of** `OPCUA_INCLUDE_NS0`.
- `OPCUA_METHOD_FILTER` regex matched against the **method** NodeId text, like `OPCUA_WRITE_FILTER`.
- Call: `RequestHeader.TimeoutHint = int.MaxValue` (largest value no stack misreads as negative). No bridge-side timeout or retry; `BadNotConnected` without a call when there is no session.
- One queue for Get, Set, Exec, Monitor, Delete, DeleteUnlisted and every refusal; only `GetSelfDescription` bypasses it. Read and Write batches stay ≤ 500. The spec's `IMonitorCommands` seam is realized as an awaited `Func<CancellationToken, Task>` given to `CommandExecutor.EnqueueAlone`; no interface.
- Logging: failed Exec → Warning with object, method, sender `CommandExchangeName`, status; success → Verbose. Discovery Info summary: `{n} executable methods have no Exec command ({reason}: {count}, …)`, reasons as `OpcUaClient.UnwritableReason` returns them.
- Every `dotnet` call needs `MsBuildSDKsPath="C:\Program Files\dotnet\sdk\10.0.402\Sdks"`. Build `StatePipes.sln`. Run `dotnet format StatePipes.sln` before each commit (CLAUDE.md rule 3).
- Test command: `dotnet test StatePipes.Test --settings tests.runsettings --filter "FullyQualifiedName~StatePipes.Test.OpcUaBridge"`.
- New integration tests use `PlcServerFixture.StartAsync(48404)` (48401–48403 are taken).

## Review Focus

1. **A method node shared by two objects** is browsed twice but the existing `visited` set drops the second reference: both pairs must still be recorded — Task 3 `Discover_SharedMethod_OneExecPerObject`.
2. **An Exec with an empty body (`""` or `{}`) for a method with no inputs** must call, not refuse — Task 4 `Encode_EmptyBodyNoInputs_Good`.
3. **A Monitor command while the coordinator handles a slow session rebuild**: the queue waits for it, and bridge shutdown never leaves the executor awaiting a task nobody completes — Task 5 `MonitorAsync_CoordinatorStopped_TaskCancelled`.
4. **Call returns Good with a null or missing output** (server bug): the property is omitted, never sent as null — Task 4 `Create_NullOutput_Omitted`.
5. **Get, Set, Get on one node arriving together** reads old, writes, reads new — Task 6 `GetSetGet_ThreeRunsInOrder`, Task 8 `SetThenGet_ReadsOwnWrite`.

---

### Task 1: Test methods in OpcUaServer and an untimed `CallAsync`

This task settles the spec's open risk first: a 20 s call must outlive the 15 s `OperationTimeout`.

**Files:**
- Modify: `OpcUaServer/PlcNodeManager.cs` (new `CreateMethods(root, densityTypeId)` called from `CreateAddressSpace`; new `RejectMethodState` nested or file-local class)
- Modify: `OpcUaServer/README.md` (method table from the spec)
- Modify: `StatePipes.Test/OpcUaBridge/PlcServerFixture.cs`
- Modify: `StatePipes.OpcUaBridge/OpcUaClient.cs`
- Test: `StatePipes.Test/OpcUaBridge/OpcUaBridgeExecTest.cs` (new, `[TestCategory(TestCategories.Integration)]`, class-level fixture on 48404)

**Interfaces — Produces:**
- `BaseObjectState` `ns=2;s=Methods` (HasComponent from `PLC_Data`), objects `Methods.Pump1`, `Methods.Pump2`, variable `Methods.NoArgsCount` (Int32, CurrentRead), and methods `Methods.Add`, `.Echo`, `.EchoDensity`, `.NoArgs`, `.Slow`, `.Reject`, `.TextOut`, `.NotExecutable`, `.UserNotExecutable`, `.MatrixIn` on `Methods`, plus `Methods.Start` as a child of `Pump1` with an extra `HasComponent` reference from `Pump2` (and the inverse on `Start`). Signatures exactly as the spec's table; argument names `a`, `b`, `sum`, `percent`, `ms`, and for Echo `d`, `i16`, `b`, `s`, `dt`, `da`, `bytes` (outputs same names). `Byte[]` is `DataTypeIds.Byte` with `ValueRanks.OneDimension`.
- `RejectMethodState : MethodState` overrides the `Call` overload that receives the argument-error list: sets error 0 to `BadOutOfRange` and returns `BadInvalidArgument` when `percent` is outside 0–100.
- `PlcServerFixture.Method(string name) => new NodeId($"Methods.{name}", NamespaceIndex)`; `MethodsObject`, `Pump1`, `Pump2` NodeIds.
- `Task<CallMethodResult> OpcUaClient.CallAsync(NodeId objectId, NodeId methodId, IReadOnlyList<Variant> inputs, CancellationToken ct)` — `new CallMethodResult { StatusCode = BadNotConnected }` without a call when not connected; otherwise one `session.CallAsync(new RequestHeader { TimeoutHint = int.MaxValue }, [request], ct)`, `ClientBase.ValidateResponse`, returns `Results[0]`. Throws on service failure.

- [ ] **Step 1: Write the failing tests** in `OpcUaBridgeExecTest` (connect an `OpcUaClient` from `_plc.CreateBridgeSettings(MonitorRegistryTest.Own)`):

```csharp
[TestMethod]
public async Task Call_Add_ReturnsSum()
{
    var result = await _client.CallAsync(_plc.MethodsObject, _plc.Method("Add"), [new Variant(2), new Variant(3)], CancellationToken.None);
    Assert.AreEqual(StatusCodes.Good, result.StatusCode.Code);
    Assert.AreEqual(5, (int)result.OutputArguments[0]);
}

[TestMethod]
public async Task Call_Slow20Seconds_OutlivesOperationTimeout()
{
    var result = await _client.CallAsync(_plc.MethodsObject, _plc.Method("Slow"), [new Variant(20_000u)], CancellationToken.None);
    Assert.AreEqual(StatusCodes.Good, result.StatusCode.Code);
}

[TestMethod]
public async Task Call_Reject150_InvalidArgumentWithOutOfRange()
{
    var result = await _client.CallAsync(_plc.MethodsObject, _plc.Method("Reject"), [new Variant(150.0)], CancellationToken.None);
    Assert.AreEqual(StatusCodes.BadInvalidArgument, result.StatusCode.Code);
    Assert.AreEqual(StatusCodes.BadOutOfRange, result.InputArgumentResults[0].Code);
}

[TestMethod]
public async Task Call_NotConnected_BadNotConnected() // fresh client, never connected
```

- [ ] **Step 2: Run** — FAIL (`CallAsync`, `Method` missing).
- [ ] **Step 3: Implement** the server nodes (one helper `AddMethod(NodeState parent, string name, Argument[] inputs, Argument[] outputs, GenericMethodCalledEventHandler2? handler, bool executable = true, bool userExecutable = true)` following the existing `CalculatePressureVolumeMass` code), the fixture members and `CallAsync`.
- [ ] **Step 4: Run** — PASS. **If `Call_Slow20Seconds_OutlivesOperationTimeout` fails with `BadTimeout`/`BadRequestTimeout`, stop and report to the user: the spec's no-timeout design must be revisited.**
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): test methods and untimed CallAsync`.

### Task 2: Exec names and argument property names

**Files:**
- Modify: `StatePipes.OpcUaBridge/OpcUaNaming.cs`
- Create: `StatePipes.OpcUaBridge/MethodArgumentNaming.cs`
- Test: `StatePipes.Test/OpcUaBridge/OpcUaBridgeTest.cs` (naming), `StatePipes.Test/OpcUaBridge/MethodArgumentNamingTest.cs` (new, Unit)

**Interfaces — Produces:**
- `MessageNames.ExecPrefix = "Exec_"`, `ExecResultPrefix = "ExecResult_"`; `ExecCommandTypeName/FullName`, `ExecResultEventTypeName/FullName`.
- `static MessageNames? MessageNames.FromExecCommandFullName(string commandFullName, string @namespace)` — `{ns}.Exec_X_Command` → `new MessageNames(ns, "Get_X")`, else null.
- `static IReadOnlyDictionary<(NodeId ObjectId, NodeId MethodId), MessageNames> OpcUaNaming.AssignUniqueMethodNames(string exchangeName, IEnumerable<(NodeId ObjectId, NodeId MethodId)> pairs, Action<string>? onCollision = null)` — qualifies every pair of a method reached from more than one object; fits and de-duplicates like `AssignUniqueNames` (share its uniqueness loop, ordered by base text).
- `static IReadOnlyList<string> MethodArgumentNaming.PropertyNames(IReadOnlyList<string?> argumentNames, IReadOnlySet<string> reserved)`; `static readonly IReadOnlySet<string> MethodArgumentNaming.OutputReserved = {"ObjectId","MethodId","StatusCode","Status"}`.

- [ ] **Step 1: Write failing tests**
  - `ExecNames_ForMethod`: `ns=2;s=CalculatePressureVolumeMass` on `Line1Plc` → `Line1Plc.Exec_ns2_s_CalculatePressureVolumeMass_Command` / `Line1Plc.ExecResult_ns2_s_CalculatePressureVolumeMass_Event`.
  - `AssignUniqueMethodNames_SharedMethod_EveryPairQualified`: pairs (Pump1, Start), (Pump2, Start), (Obj, Add) → `Exec_ns2_s_Pump1__ns2_s_Start_Command`, `Exec_ns2_s_Pump2__ns2_s_Start_Command`, `Exec_ns2_s_Add_Command`.
  - `AssignUniqueMethodNames_SanitizeCollision_Suffixed`: methods `A-B` and `A_B` → one bare, one `_2`.
  - `AssignUniqueMethodNames_VeryLong_FitsRoutingKey`: ExecResult and Exec full names ≤ 255 for a 400-char object and method id.
  - `FromExecCommandFullName_RecognisesExecOnly`: Exec name → names; Set, Monitor, Get names → null; `FromSetCommandFullName` and `FromCommandFullName` of an Exec name → null.
  - `PropertyNames_SanitizedAndUnique`: `["a", "a", "", "1x", "b.c", null]` → `["a", "a_2", "Arg2", "_1x", "b_c", "Arg5"]`.
  - `PropertyNames_OutputReserved_Suffixed`: with `OutputReserved`, `["Status", "sum"]` → `["Status_2", "sum"]`; names are assigned in argument order, so `["Status", "Status_2"]` → `["Status_2", "Status_2_2"]`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.** Sanitize with `OpcUaNaming.Sanitize`; empty → `Arg{index}`; leading digit → `_` prefix; then suffix `_2`, `_3`… until the name is neither reserved nor taken.
- [ ] **Step 4: Run the OpcUaBridge filter** — PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): Exec and ExecResult names`.

### Task 3: Method discovery

**Files:**
- Create: `StatePipes.OpcUaBridge/OpcUaMethodItem.cs` (both records below)
- Modify: `StatePipes.OpcUaBridge/OpcUaClient.cs`, `BridgeSettings.cs`, `ServiceWorker.cs`
- Test: `StatePipes.Test/OpcUaBridge/OpcUaBridgeExecTest.cs`, `OpcUaBridgeSetTest.cs` (its `DiscoverAsync` helper returns `.Items`)

**Interfaces — Produces:**
- `internal sealed record MethodArgument(string PropertyName, BuiltInType BuiltInType, Type ValueType, StructureLayout? Structure, bool IsArray)` — `ValueType` from `OpcUaValueMapper.MapToClrType(builtInType, rank)`.
- `internal sealed record OpcUaMethodItem(NodeId ObjectId, NodeId MethodId, string ObjectIdText, string MethodIdText, MessageNames Names, IReadOnlyList<MethodArgument> Inputs, IReadOnlyList<MethodArgument> Outputs)` with `ExecCommandTypeFullName`, `ExecResultEventTypeFullName`.
- `internal sealed record DiscoveredNodes(IReadOnlyList<OpcUaDataItem> Items, IReadOnlyList<OpcUaMethodItem> Methods)`; `Task<DiscoveredNodes> OpcUaClient.DiscoverAsync(CancellationToken)`; methods ordered by `ExecCommandTypeFullName`.
- `BridgeSettings.MethodFilter` (`Regex?`) from `OPCUA_METHOD_FILTER`.
- `ServiceWorker.MaintainOpcUaSessionAsync` calls `bridge.SetCatalog(nodes.Items, nodes.Methods)` (signature added in Task 7; until then pass only `nodes.Items`).

- [ ] **Step 1: Write failing tests** in `OpcUaBridgeExecTest`:
  - `Discover_MethodsTable_ExecExactlyAsSpec`: methods whose MethodId starts with `Methods.` are exactly Add, Echo, EchoDensity, NoArgs, Slow, Reject, TextOut, and Start twice (objects Pump1 and Pump2); NotExecutable, UserNotExecutable and MatrixIn absent; `CalculatePressureVolumeMass` present.
  - `Discover_Arguments_Typed`: Add inputs `a`,`b` Int32 → `typeof(int)`, output `sum`; Echo `da` is `double[]`, `bytes` is `byte[]` with `IsArray`; EchoDensity inputs have non-null `Structure`, second `IsArray`; TextOut output `ValueType == typeof(string)`; NoArgs has no inputs or outputs.
  - `Discover_SharedMethod_OneExecPerObject`: the two Start items have ObjectIds Pump1 and Pump2 and the same MethodId.
  - `Discover_NoServerOwnMethods`: no method has namespace index 0 or the Diagnostics namespace, also with `IncludeNamespaceZero = true`.
  - `Discover_MethodFilter_Narrows`: `MethodFilter = new Regex("Methods\\.Add$")` → only Add among `Methods.*`.
  - `Discover_UnencodableInput_SummaryLogged`: a recording logger sees `1 executable methods have no Exec command (matrix or unfixed rank: 1)` (count only `Methods.*`-caused lines if the server adds others; assert the line contains `matrix or unfixed rank`).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.**
  - Browse: add `NodeClass.Method` to the mask; track each browsed node's NodeClass; for a Method reference whose source node is an Object, record `(source, method)` **before** the `visited` check, and never enqueue methods.
  - Keep a pair when the method passes the namespace rule, `MethodFilter`, and `Executable` and `UserExecutable` read true (read both attributes in batches like `ReadVariableAttributesAsync`).
  - Arguments: browse each kept method's `HasProperty` references with `BrowseResultMask.BrowseName`, read the `InputArguments`/`OutputArguments` values as `Argument[]` (missing → none). For each argument resolve `BuiltInType` via `TypeInfo.GetBuiltInTypeAsync` and a structure layout via the same `StructureLayoutBuilder` instance the variables use (hoist it to `DiscoverAsync`).
  - Drop a method when any input has `UnwritableReason(...) != null`; count reasons and log the summary; Verbose per method.
  - Property names via `MethodArgumentNaming.PropertyNames` (outputs with `OutputReserved`); names via `AssignUniqueMethodNames` (collision → Warning, like items).
- [ ] **Step 4: Run the OpcUaBridge filter** — PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): discover executable methods`.

### Task 4: `ExecArgumentEncoder` and `ExecResultBody`

**Files:**
- Create: `StatePipes.OpcUaBridge/ExecArgumentEncoder.cs`, `StatePipes.OpcUaBridge/ExecResultBody.cs`
- Modify: `StatePipes.OpcUaBridge/SelfDescriptionBuilder.cs` (two constants only)
- Test: `StatePipes.Test/OpcUaBridge/ExecArgumentEncoderTest.cs`, `ExecResultBodyTest.cs` (new, Unit)

**Interfaces — Produces:**
- `static StatusCode ExecArgumentEncoder.TryEncode(byte[] body, IReadOnlyList<MethodArgument> inputs, out IReadOnlyList<Variant> values)` — parse with `DateParseHandling.None`; empty/whitespace body is `{}`; non-object → `BadArgumentsMissing`; any input missing → `BadArgumentsMissing`; then any extra property → `BadTooManyArguments`; then each input in order through `StructureEncoder.TryEncode` or `OpcUaValueEncoder.TryEncode` (first Bad wins).
- `static byte[] ExecResultBody.Create(OpcUaMethodItem method, CallMethodResult result)` and `static byte[] ExecResultBody.Refused(OpcUaMethodItem? method, StatusCode status)` (empty ids when `method` is null). Outputs converted exactly as `OpcUaBridgeService.CreateGetEventBody` converts `DataValue.WrappedValue` (extract that conversion into a shared `internal static JToken ToJToken(Variant value, Type valueType, StructureLayout? structure, bool isArray)` on `OpcUaValueMapper` and use it from both).

- [ ] **Step 1: Write failing tests**
  - `Encode_AddInputs_TypedVariants`: `{"a":2,"b":3}` → two Int32 Variants 2, 3, in input order even when the JSON order is `b`, `a`.
  - `Encode_EmptyBodyNoInputs_Good` (`""`, `"{}"`), `Encode_Missing_BadArgumentsMissing`, `Encode_NotObject_BadArgumentsMissing` (`"[1]"`, `"5"`, `"x"`), `Encode_Extra_BadTooManyArguments`, `Encode_WrongKind_BadTypeMismatch` (`{"a":"2","b":3}`), `Encode_OutOfRange_BadOutOfRange` (`{"a":3000000000,"b":0}`), `Encode_Structure_ExtensionObject` (Density layout from `OpcUaStructureTest`'s helpers).
  - `Create_Good_OutputsAndIds`: Add item, result Good with output 5 → `{"ObjectId":"ns=2;s=Methods","MethodId":"ns=2;s=Methods.Add","StatusCode":0,"Status":"Good","sum":5}`.
  - `Create_Bad_NoOutputs`; `Create_Uncertain_OutputsIncluded`; `Create_NullOutput_Omitted`.
  - `Create_InvalidArgument_NamesRejectedInputs`: Status equals `"BadInvalidArgument: percent (BadOutOfRange)"` (build the expected prefix from `new StatusCode(StatusCodes.BadInvalidArgument).ToString()`).
  - `Refused_NullMethod_EmptyIds`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement** as above.
- [ ] **Step 4: Run the OpcUaBridge filter** — PASS (Get event tests prove the extracted conversion).
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): encode Exec inputs and build ExecResult`.

### Task 5: Awaitable Monitor and Delete on `MonitorCoordinator`

**Files:**
- Modify: `StatePipes.OpcUaBridge/MonitorCoordinator.cs`, `OpcUaBridgeService.cs` (call sites, discard the task for now)
- Test: `StatePipes.Test/OpcUaBridge/MonitorCoordinatorTest.cs`

**Interfaces — Produces:**
- `Task MonitorCoordinator.MonitorAsync(string senderCommandExchange, OpcUaDataItem item, MonitorParameters parameters, string replyToRaw, CancellationToken ct)`, `Task DeleteAsync(string senderCommandExchange, OpcUaDataItem item, string replyToRaw, CancellationToken ct)`, `Task DeleteUnlistedAsync(string senderCommandExchange, MessageNames names, string replyToRaw, StatusCode notFound, CancellationToken ct)` replace `Monitor`, `Delete`, `DeleteUnlisted`. Each request record carries a `TaskCompletionSource` (`RunContinuationsAsynchronously`) completed after its handler, also when the handler throws; work left when `RunAsync` ends is cancelled; the returned task is `tcs.Task.WaitAsync(ct)`.

- [ ] **Step 1: Write failing tests**
  - `MonitorAsync_CompletesAfterStatusSent`: the task completes and the output already holds the MonitorStatus.
  - `MonitorAsync_WaitsForQueuedSessionStart`: with a fake session whose `ApplyAllAsync` blocks on a gate, a SessionStarted then MonitorAsync → task not complete until the gate opens.
  - `MonitorAsync_HandlerThrows_StillCompletes`.
  - `MonitorAsync_CoordinatorStopped_TaskCancelled`: cancel `RunAsync`, then a pending and a new request → both tasks end `TaskCanceledException` within 1 s.
  - Existing coordinator tests switch to the async names (`await` them).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the OpcUaBridge filter** — PASS.
- [ ] **Step 5: `dotnet format`, commit** `refactor(opcua-bridge): awaitable Monitor and Delete`.

### Task 6: `CommandExecutor` replaces `SetWriter` and the Get loop

**Files:**
- Create: `StatePipes.OpcUaBridge/CommandExecutor.cs`, `IOpcUaSession.cs`, `ICommandOutput.cs`
- Delete: `StatePipes.OpcUaBridge/SetWriter.cs`, `ISetSession.cs`
- Modify: `StatePipes.OpcUaBridge/OpcUaClient.cs` (implements `IOpcUaSession`), `OpcUaBridgeService.cs`
- Test: `git mv StatePipes.Test/OpcUaBridge/SetWriterTest.cs CommandExecutorTest.cs`; update `OpcUaBridgeSetRoutingTest.cs` fake to `IOpcUaSession`

**Interfaces — Consumes:** Task 4 `ExecArgumentEncoder`, `ExecResultBody`; Task 5 async monitor methods.

**Interfaces — Produces:**
- `interface IOpcUaSession { Task<IReadOnlyList<DataValue>> ReadValuesAsync(IReadOnlyList<NodeId>, CancellationToken); Task<IReadOnlyList<StatusCode>> WriteValuesAsync(IReadOnlyList<(NodeId NodeId, Variant Value)>, CancellationToken); Task<CallMethodResult> CallAsync(NodeId objectId, NodeId methodId, IReadOnlyList<Variant> inputs, CancellationToken); }`
- `interface ICommandOutput { void SendGetEvent(OpcUaDataItem item, DataValue value, BusConfig replyTo, byte[] replyToRaw); void SendSetStatus(OpcUaDataItem item, StatusCode status, BusConfig replyTo, byte[] replyToRaw); void SendExecResult(MessageNames names, byte[] body, BusConfig replyTo, byte[] replyToRaw); }`
- `sealed class CommandExecutor(IOpcUaSession session, ICommandOutput output)` with `EnqueueGet(OpcUaDataItem, BusConfig, byte[])`, `EnqueueSet(OpcUaDataItem, byte[] body, BusConfig, byte[])`, `EnqueueExec(OpcUaMethodItem, byte[] body, BusConfig, byte[])`, `EnqueueAlone(string description, Func<CancellationToken, Task> run)`, `Task RunAsync(CancellationToken)`, `void Complete()`.
- `OpcUaBridgeService(BusConfig, OpcUaClient, MonitorRegistry, TimeProvider? time = null, ConnectionChannelFactory? channelFactory = null, IOpcUaSession? session = null)` — `session` replaces `setSession`.

- [ ] **Step 1: Write failing tests** in `CommandExecutorTest` (fake session recording each call in one ordered log; fake output recording answers in order). Keep every former `SetWriterTest` case, renamed onto the executor. Add:
  - `GetSetGet_ThreeRunsInOrder`: log is Read, Write, Read; answers Get, SetStatus, Get.
  - `ConsecutiveGets_OneRead`, `ConsecutiveSets_OneWrite` (≤ 500 each, 501 → two).
  - `Exec_AlwaysAlone`: Exec, Exec → two Calls; Set, Exec, Set → Write, Call, Write.
  - `Exec_EncodingFails_AnsweredWithoutCall`; `Exec_CallThrows_CommunicationErrorThenQueueContinues`; `Exec_ServiceResultException_ItsCode`; `Exec_Cancelled_NoAnswer`.
  - `Alone_NextRunWaitsForTask`: an `EnqueueAlone` whose task waits on a gate, then a Get → no Read until the gate opens.
  - `Alone_Throws_LoggedAndQueueContinues`.
  - `SlowExec_HoldsBackLaterCommands`: Call gated; a later Get and Set are not served until it returns.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement `CommandExecutor`.** Reader loop: peek the head kind; Gets/Sets drain while the next item is the same kind (Sets also stop on a repeated node, keeping the held-over item exactly as `SetWriter` did); answer every member before the next run. Move `SetWriter`'s encode/answer/log code in unchanged; Get answers use `ICommandOutput.SendGetEvent`; Read exceptions handled as `OpcUaBridgeService.ReadAsync` does today. Exec: encode → call → `SendExecResult(method.Names, …)`, Warning/Verbose logging per Global Constraints.
- [ ] **Step 4: Wire the bridge.** `OpcUaBridgeService` implements `ICommandOutput` (Get events and SetStatus through the existing `SendResponse`; ExecResult routing key `names.ExecResultEventTypeFullName`); `ConsumeCommand` enqueues Gets and Sets on the executor; Monitor, Delete, DeleteUnlisted, Monitor parse failures and every Set/Monitor/Delete refusal become `EnqueueAlone` lambdas (refusals send the same answers as today). `RunAsync` runs `_executor.RunAsync` instead of `ServeGetsAsync` and `SetWriter.RunAsync`; `Dispose` calls `Complete()`. Remove `_pendingGets`, `PendingGet`, `ServeGetsAsync`.
- [ ] **Step 5: Run the OpcUaBridge filter** — all PASS, including `OpcUaBridgeTest`, `OpcUaBridgeSetTest`, `OpcUaBridgeSetRoutingTest` and the monitor tests unchanged except for the session fake type.
- [ ] **Step 6: `dotnet format`, commit** `refactor(opcua-bridge): one ordered queue for every command`.

### Task 7: Exec in the self-description and routing

**Files:**
- Modify: `StatePipes.OpcUaBridge/SelfDescriptionBuilder.cs`, `OpcUaBridgeService.cs`, `ServiceWorker.cs`
- Test: `StatePipes.Test/OpcUaBridge/OpcUaBridgeTest.cs` (self-description), `StatePipes.Test/OpcUaBridge/OpcUaBridgeExecRoutingTest.cs` (new, Unit, harness copied from `OpcUaBridgeSetRoutingTest`)

**Interfaces — Produces:**
- `SelfDescriptionBuilder.Build(IEnumerable<OpcUaDataItem> items, IEnumerable<OpcUaMethodItem>? methods = null)`: per method an Exec command (inputs: structure → `AddStructureProperty`, else `AddProperty(name, ValueType)`, not nullable) and an ExecResult event (`ObjectId`, `MethodId` string, `StatusCode` uint, `Status` string, outputs typed like a Get `Value`: nullable for value types, structures via `AddStructureProperty`).
- `OpcUaBridgeService.SetCatalog(IReadOnlyList<OpcUaDataItem> items, IReadOnlyList<OpcUaMethodItem>? methods = null)`; catalog `ByExecCommand`; log line adds `and {m} methods as Exec commands`.
- `ConsumeCommand`: an Exec in `ByExecCommand` → `TryGetAllowedReplyTo` then `EnqueueExec` with a body copy; else `FromExecCommandFullName` → guard → `EnqueueAlone` sending `ExecResultBody.Refused(null, !Discovered ? BadWaitingForInitialData : BadMethodInvalid)`.

- [ ] **Step 1: Write failing tests**
  - `SelfDescription_ExecTypesOnlyForMethods`: Build with Add → both full names present with the property lists above.
  - `SelfDescription_ConsumedLikeExplorer_ExecTypesEmit` (extend the existing Explorer-consumption test): emitted Exec type for Add serializes `{a:2,b:3}` that `ExecArgumentEncoder` accepts; an `ExecResultBody.Create` body deserializes into the emitted ExecResult type with `sum == 5`.
  - `SelfDescription_SizePerMethod`: like `SelfDescription_SizePerWritableItem`, 100 Add-shaped methods; print bytes per method to the test log for the README.
  - Routing: `Exec_Known_CalledAndAnswered`; `Exec_BeforeDiscovery_BadWaitingForInitialData`; `Exec_Unknown_BadMethodInvalid` with empty ids; `Exec_GuardFails_NoCallNoAnswer`; `RefusalsAnsweredInArrivalOrder` (gated Call, then an unknown Exec and an unknown Set → answers after the Call's, in that order).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement**, and `ServiceWorker` passes `nodes.Methods`.
- [ ] **Step 4: Run the OpcUaBridge filter** — PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): route Exec commands and describe them`.

### Task 8: End-to-end Exec and ordering; docs

**Files:**
- Test: `StatePipes.Test/OpcUaBridge/OpcUaBridgeExecTest.cs` (bridge harness as in `OpcUaBridgeSetTest`: `Service`, `Channels`, `MaintainOpcUaSessionAsync`)
- Modify: `StatePipes.OpcUaBridge/README.md`

- [ ] **Step 1: Write the tests**
  - `Exec_EachMethod_ExpectedOutputs`: Add 2+3 → `sum` 5; Echo returns every input unchanged (DateTime ISO 8601 UTC, bytes base64); EchoDensity returns its structures; NoArgs with `{}` → Good, no outputs; TextOut → a string; Start via Pump1 → Good.
  - `Exec_Reject150_NamesPercent`: Status `"BadInvalidArgument: percent (BadOutOfRange)"`.
  - `Exec_Slow20000_Succeeds` (timeout ≥ 30 s on the wait).
  - `Ordering_SlowNoArgsGet_AnsweredInOrderAndSeesIncrement`: send `Slow` {ms:2000}, `NoArgs`, Get `NoArgsCount` back-to-back → answers arrive in that order; the Get value is the count before the test + 1.
  - `SetThenGet_ReadsOwnWrite`: Set `Setpoints.Double` = 42.5 and Get it back-to-back → Get returns 42.5.
- [ ] **Step 2: Run** — PASS (all behaviour exists; a failure is a bug in Tasks 3–7, fix it there).
- [ ] **Step 3: README**: Exec/ExecResult messages with the Add example; `OPCUA_METHOD_FILTER`; bridge-wide ordering of every command and answer; no call timeout — a hung method stalls commands but not notifications; outcome-unknown codes; a lost answer puts order matching out of step; which methods get no Exec and why; the measured bytes per method from Task 7.
- [ ] **Step 4: Full suite** `dotnet test StatePipes.sln --settings tests.runsettings` — PASS.
- [ ] **Step 5: `dotnet format`, commit** `test(opcua-bridge): end-to-end Exec and ordering; docs`.
- [ ] **Step 6: Manual end-to-end** (report results; no commit): run `OpcUaServer` and the bridge against the local broker, open the bridge in StatePipes.Explorer, call each `Methods` method.
