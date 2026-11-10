# Persisted Workflows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add durable, CorrelationId-routed, multi-instance workflows to StatePipes, persisted in PostgreSQL over client-certificate auth, with durable timers, infrastructure scripts, a test harness and tooling.

**Architecture:** A workflow is an `IWorkflow` state machine whose states have parameterless constructors; every state field is persisted as a `jsonb` document. Each command builds a fresh `BaseStateMachine` from cached per-type metadata, rehydrates fields, fires, and saves inside one PostgreSQL transaction guarded by `pg_try_advisory_xact_lock`. All timers go through an `ITimerService`: in-memory for singleton machines, a persisted table plus a `SKIP LOCKED` poller for workflows.

**Tech Stack:** .NET 10 / C#, Stateless 5.20.1, Autofac, Newtonsoft.Json, Mono.Cecil, Npgsql, MSTest + FakeItEasy + `Microsoft.Extensions.TimeProvider.Testing`, PowerShell, Docker (`postgres:17`, `smallstep/step-ca`).

**Spec:** `docs/superpowers/specs/2026-11-08-persisted-workflows-design.md` — read it alongside this plan; section references (§) point there.

## Global Constraints

- Build: `dotnet build`; tests: `dotnet test --settings Tests.runsettings`; single test: `dotnet test --settings Tests.runsettings --filter FullyQualifiedName~<Name>`.
- Run `dotnet format` before every commit (CLAUDE.md Rule 3).
- Never call `.Result` / `.Wait()` in state machine code or loops (Rule 1). Use Npgsql's **synchronous** API on the bus thread.
- Every new test class carries `[TestCategory(TestCategories.Unit)]` or `[TestCategory(TestCategories.Integration)]`; the test assembly is `[assembly: DoNotParallelize]`.
- Integration tests call `Assert.Inconclusive` when `%ProgramData%\SmallStep\step-ca\statepipes.postgres-db.client.p12` is absent.
- Values fixed by the spec: schema `statepipes`; tables `workflow_instance`, `workflow_timer` (§5.1 columns verbatim); defaults `Port=5432`, `PasswordFileName="CommonPassword.txt"`, `LockedMaxRetries=10`, `LockedRetryDelay=50 ms`, `TimerPollInterval=1 s`; poller batch `100`; container `postgres-db`; image `postgres:17`; DB and role `statepipes`; client bundle `statepipes.postgres-db.client.p12`; volume `postgres-db-data`; TLS minimum `TLSv1.3`.
- Locked log text starts with `Workflow command not satisfied: workflow locked`.
- Workflow transitions never publish `StateStatusEvent`; workflows are never registered in `StateMachineManager` at runtime.
- `StatePipes` has `InternalsVisibleTo("StatePipes.Test")`; new runtime types are `internal` unless the spec lists them as public (§3.1, `WorkflowStoreConfig`, `WorkflowForTest<T>`).
- New workflow code lives under `StatePipes/Workflow/` (namespace `StatePipes.Workflow` for public, `StatePipes.Workflow.Internal` for internal); tests under `StatePipes.Test/Workflow/`.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Empty/null CorrelationId** on a workflow command → logged as an error and discarded; no instance with an empty key is ever stored. (Test in Task 10.)
2. **Stale timer message after the instance finished** (a `CartAbandoned` claimed by the poller just as checkout deleted the cart) → must not resurrect the cart through implicit creation. Commands delivered by the timer poller are flagged, and the engine never creates an instance for them. (This narrows D9 for timer deliveries only. Test in Task 10.)
3. **Advisory lock key stability**: the key must be identical across processes and machines; `string.GetHashCode()` is randomized per process. (A fixed-value test in Task 14.)
4. **Duplicate state class names within one workflow** (same `Name`, different namespaces) would collide as Stateless keys and document keys → startup validation error. (Test in Task 5.)
5. **A field whose runtime value is a type outside the binder's allow-list** → the save fails, the transaction rolls back, and the error names the state and field. (Test in Task 6.)

---

## File Structure

| Path | Responsibility |
|---|---|
| `StatePipes/Common/Internal/ITimerService.cs` | Timer-thread interface (§6.1). |
| `StatePipes/Common/Internal/InMemoryTimerService.cs` | Single-thread priority-queue timers + `ScheduleCallback`. |
| `StatePipes/Common/Internal/DelayedMessageSender.cs` (modify) | Thin wrapper over `ITimerService`. |
| `StatePipes/Interfaces/IDelayedMessageSender.cs` (modify) | Adds `TimerId`. |
| `StatePipes/Interfaces/IWorkflow.cs`, `IFinalWorkflowState.cs` | Public markers. |
| `StatePipes/StateMachine/BaseWorkflowTriggerCommand.cs`, `BaseWorkflowState.cs`, `ParentedBaseWorkflowState.cs` | Public authoring bases. |
| `StatePipes/StateMachine/Internal/StateHandlerScanner.cs` | Attribute and Cecil scanning shared by both state bases. |
| `StatePipes/Comms/WorkflowStoreConfig.cs` | Public config (§5.5). |
| `StatePipes/Workflow/Internal/WorkflowMetadata.cs` | Per-type cache + validation. |
| `StatePipes/Workflow/Internal/WorkflowJson.cs` | Settings, contract resolver, binder, sender converter. |
| `StatePipes/Workflow/Internal/WorkflowDocument.cs` | Extract and populate state fields ↔ document JSON. |
| `StatePipes/Workflow/Internal/IWorkflowStore.cs` | Store / unit-of-work interfaces + records. |
| `StatePipes/Workflow/Internal/InMemoryWorkflowStore.cs` | Test store. |
| `StatePipes/Workflow/Internal/PeriodicSchedule.cs` | Skip-missed computation. |
| `StatePipes/Workflow/Internal/WorkflowContext.cs` | Thread-static per-command context. |
| `StatePipes/Workflow/Internal/WorkflowTimerService.cs` | `ITimerService` over the current unit of work. |
| `StatePipes/Workflow/Internal/WorkflowTimerPoller.cs` | Firing thread. |
| `StatePipes/Workflow/Internal/WorkflowMachineFactory.cs` | Builds the per-command machine. |
| `StatePipes/Workflow/Internal/WorkflowEngine.cs` | The §4.3 pipeline. |
| `StatePipes/Workflow/Internal/BaseWorkflowTriggerCommandHandler.cs` | Routes to the engine. |
| `StatePipes/Workflow/Internal/AllWorkflowContainerSetup.cs` | Discovery, registration, poller start. |
| `StatePipes/Comms/Internal/CommandDispatchContext.cs` | Thread-static retry count / timer-delivery flag for the message in flight. |
| `StatePipes/Workflow/Internal/Postgres/PostgresConnectionFactory.cs`, `WorkflowSchema.cs`, `PostgresWorkflowStore.cs`, `AdvisoryLockKey.cs` | PostgreSQL implementation. |
| `StatePipes/StateMachine/Test/WorkflowForTest.cs` | Public harness. |
| `StatePipes.Test/Workflow/Samples/*` | Sample workflows (Cart + deliberately invalid ones). |

---

## Phase A — Timers

### Task 1: `ITimerService` and `InMemoryTimerService`

**Files:**
- Create: `StatePipes/Common/Internal/ITimerService.cs`, `StatePipes/Common/Internal/InMemoryTimerService.cs`
- Test: `StatePipes.Test/Common/InMemoryTimerServiceTest.cs`

**Interfaces:**
- Produces:
```csharp
internal interface ITimerService
{
    void StartOneShot(Guid timerId, TimeSpan dueTime, IMessage message);
    void StartPeriodic(Guid timerId, TimeSpan period, IMessage message);
    void Stop(Guid timerId);
    bool IsEnabled(Guid timerId);
}
internal sealed class InMemoryTimerService(IMessageSender sender, TimeProvider timeProvider) : ITimerService, IDisposable
{
    public void ScheduleCallback(TimeSpan dueTime, Action callback);   // one-shot, not cancellable, no TimerId
}
```

- [ ] **Step 1: Write failing tests** (each uses a `FakeTimeProvider`, a recording `IMessageSender` fake, and polls up to 1 s for the timer thread to deliver; the delivered message type is a test `record Ping : IMessage`):
  - `OneShot_FiresOnceAtDueTime`: start at 5 s; advance 4.9 s → 0 sends; advance 0.1 s → 1 send; advance 10 s → still 1; `IsEnabled` is `false` afterwards.
  - `Periodic_FiresEachPeriodWithoutDrift`: period 1 s; advance 1 s ×3 → 3 sends; `IsEnabled` stays `true`.
  - `Start_SameTimerId_ReplacesPending`: start at 5 s, restart at 10 s; advance 6 s → 0 sends; advance 4 s → 1.
  - `Stop_CancelsAndDisables`: start at 5 s, stop; advance 10 s → 0 sends; `IsEnabled` is `false`.
  - `ScheduleCallback_InvokesOnce`: after 50 ms of fake time the callback has run exactly once.
  - `SenderThrows_ThreadSurvives`: the first send throws; a second timer still fires.
- [ ] **Step 2: Run** `dotnet test --settings Tests.runsettings --filter FullyQualifiedName~InMemoryTimerServiceTest` → compile failure (types missing).
- [ ] **Step 3: Implement.** One dedicated thread started in the constructor. A `PriorityQueue<Guid, DateTimeOffset>` plus a `Dictionary<Guid, Entry>` holding the due time, period and message (callbacks use a fresh `Guid`). The thread waits on an `AutoResetEvent` with a timeout until the earliest due time, read from `timeProvider.GetUtcNow()`; it is signalled on every change. A `FakeTimeProvider` is supported by also waking on `timeProvider.CreateTimer(...)` for the earliest due time. Stale queue entries (replaced or stopped) are skipped by comparing the due time stored in the dictionary. A periodic entry's next due time is `previousDue + period`. Exceptions from a send or callback are logged with `Log?.LogException` and swallowed. `Dispose` stops the thread.
- [ ] **Step 4: Run the filter again** → all PASS.
- [ ] **Step 5: `dotnet format` and commit** — `feat(timers): in-memory timer thread service`.

### Task 2: `IDelayedMessageSender.TimerId` and singletons on the timer thread

**Files:**
- Modify: `StatePipes/Interfaces/IDelayedMessageSender.cs`, `StatePipes/Common/Internal/DelayedMessageSender.cs`, `StatePipes/StateMachine/Internal/BaseStateMachine.cs:22-35,90-93`, `StatePipes/StateMachine/Internal/BaseStateMachineAndFirstStateContainerSetup.cs:45-51` (`RegisterOnce`), `StatePipes/Comms/Internal/StatePipesService.cs:15,96` (heartbeat), `StatePipes/StateMachine/Test/StateMachineForTest.cs`
- Test: `StatePipes.Test/Common/DelayedMessageSenderTest.cs`, `StatePipes.Test/StateMachine/SingletonMachineCharacterizationTest.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces:
```csharp
public interface IDelayedMessageSender<TMessage> where TMessage : class, IMessage
{
    Guid TimerId { get; }
    bool Enabled { get; }
    void StartOneShot(TimeSpan dueTime, TMessage message);
    void StartPeriodic(TimeSpan period, TMessage message);
    void Stop();
}
internal class DelayedMessageSender<TMessage>(ITimerService timers, Guid timerId) : IDelayedMessageSender<TMessage>, IDisposable
// [Newtonsoft.Json.JsonIgnore] on Enabled; Dispose() => Stop()
internal BaseStateMachine(IStatePipesService bus, InMemoryTimerService timers)   // existing ctor gains timers
```
- `RegisterOnce` registers `TimeProvider.System` as `TimeProvider` (`IfNotRegistered`) and `InMemoryTimerService` as `SingleInstance`, constructed from the container's `IStatePipesService` and `TimeProvider`. `StatePipesService` owns a private `InMemoryTimerService(this, TimeProvider.System)` for its heartbeat. `StateMachineForTest` gains `public FakeTimeProvider? TimeProvider` (when set before `ConfigureStateMachine`, it is registered instead of `System`).

- [ ] **Step 1: Write failing tests.**
  - `DelayedMessageSenderTest.Delegates_ToTimerService`: with a FakeItEasy `ITimerService`, `StartOneShot(5s, m)` calls `timers.StartOneShot(id, 5s, m)`; `Enabled` returns `timers.IsEnabled(id)`; `Dispose()` calls `Stop(id)`.
  - `DelayedMessageSenderTest.Enabled_IsNotSerialized`: `JsonConvert.SerializeObject(sender)` contains `"TimerId"` and does not contain `"Enabled"`.
  - `SingletonMachineCharacterizationTest` (a characterization net for Task 3 and for this change; uses `StateMachineForTest` and a sample `PumpMachine` in `StatePipes.Test/StateMachine/Samples/`): `PermitIf_Transitions`, `PermitIf_GuardFalse_Stays`, `IgnoreIf_Consumes`, `PermitReentryIfFalse_RunsBodyNoEntryExit`, `PermitReentryIfTrue_ReentersParent_MovesToFirstSubstate`, `PeriodicTimer_FiresTriggerWithFakeTime` (a state starts a 1 s periodic sender in `OnEntry`; advancing 3 s yields 3 trigger invocations).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~DelayedMessageSenderTest|FullyQualifiedName~SingletonMachineCharacterizationTest"` → compile failure on `TimerId`.
- [ ] **Step 3: Implement** the signatures above. `BaseStateMachine.CreateDelayedMessageSender` returns `new DelayedMessageSender<T>(_timers, Guid.NewGuid())`. Delete the `System.Threading.Timer` code.
- [ ] **Step 4: Run** those tests plus the whole suite: `dotnet test --settings Tests.runsettings` → all PASS (regression, spec §10.2).
- [ ] **Step 5: Format and commit** — `feat(timers): IDelayedMessageSender.TimerId; singleton machines use the timer thread`.

---

## Phase B — Authoring model

### Task 3: Extract `StateHandlerScanner`

**Files:**
- Create: `StatePipes/StateMachine/Internal/StateHandlerScanner.cs`
- Modify: `StatePipes/StateMachine/BaseStateMachineState.cs` (move `Handle*Method`, `GetFirstSubstateType`, `GetTypeDefinition`, `GetEventTypesForState`, `GetCommandTypesForState`, `IsCommandForAnotherStateMachine`, `GetStateMachineTypeArgument` out)
- Test: `StatePipes.Test/StateMachine/StateHandlerScannerTest.cs`

**Interfaces:**
- Produces:
```csharp
internal enum HandlerKind { PermitIf, IgnoreIf, PermitReentryIf }
internal sealed record HandlerDeclaration(Type TriggerType, HandlerKind Kind, MethodInfo Method, Type? Destination, string? GuardDescription);
internal static class StateHandlerScanner
{
    public static IReadOnlyList<HandlerDeclaration> GetHandlers(Type stateType);
    public static Type? GetFirstSubstateType(Type stateType, Type parentedOpenGeneric);   // ParentedBaseStateMachineState<,> or ParentedBaseWorkflowState<,>
    public static IEnumerable<Type> GetEventTypes(Type stateType, string methodName);     // "PublishEvent" / "SendResponse" / "SendCommand"
    public static IEnumerable<Type> GetCommandTypes(Type stateType, Type stateBaseOpenGeneric); // excludes the machine's own triggers
    public static void Configure(object state, StateConfigurationWrapper config, Func<Type, ITrigger?> getTrigger, Func<BusConfig?> getResponseInfo, Type parentedOpenGeneric, Type stateBaseOpenGeneric);
}
```

- [ ] **Step 1: Write failing tests** against the `PumpMachine` sample states: `GetHandlers_ReportsKindTriggerDestination` (asserts the exact set for one state), `GetFirstSubstateType_FindsIFirstSubstateChild`, `GetEventTypes_FindsPublishEventGenericArgs`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~StateHandlerScannerTest` → compile failure.
- [ ] **Step 3: Move the code** and make `BaseStateMachineState.Configure` call `StateHandlerScanner.Configure(this, …, typeof(ParentedBaseStateMachineState<,>), typeof(BaseStateMachineState<>))`. Behaviour is unchanged.
- [ ] **Step 4: Run** the new tests and `SingletonMachineCharacterizationTest` → PASS.
- [ ] **Step 5: Format and commit** — `refactor(statemachine): share handler scanning between state bases`.

### Task 4: Public workflow types; singleton discovery skips workflows

**Files:**
- Create: `StatePipes/Interfaces/IWorkflow.cs`, `StatePipes/Interfaces/IFinalWorkflowState.cs`, `StatePipes/StateMachine/BaseWorkflowTriggerCommand.cs`, `StatePipes/StateMachine/BaseWorkflowState.cs`, `StatePipes/StateMachine/ParentedBaseWorkflowState.cs`, `StatePipes/Workflow/Internal/WorkflowContext.cs`
- Modify: `StatePipes/StateMachine/Internal/AllStateMachineContainerSetup.cs:15-16`, `StatePipes/StateMachine/Internal/AllStateMachineDummyContainerSetup.cs` (same filter)
- Test: `StatePipes.Test/Workflow/WorkflowTypesTest.cs`; samples in `StatePipes.Test/Workflow/Samples/Cart/` (the full §3.3 Cart example; `QuotePrice` is a `BaseTriggerCommand<PricingMachine>` with a `CorrelationId` property, plus a minimal `PricingMachine` singleton)

**Interfaces:**
- Produces:
```csharp
public interface IWorkflow : IStateMachine { }
public interface IFinalWorkflowState { }
public class BaseWorkflowTriggerCommand<TWorkflow> : ITrigger where TWorkflow : IWorkflow { public string CorrelationId { get; init; } = string.Empty; }
public class BaseWorkflowState<TWorkflow> : IStateMachineState where TWorkflow : IWorkflow
{   // protected: CorrelationId, SendCommand, PublishEvent, SendResponse, CreateDelayedMessageSender<T>() (non-null),
    // GetCurrentTrigger<T>(), GetCurrentResponseInfo(); public virtual OnEntry/OnExit/OnActivate/OnDeActivate; public virtual Configure(StateConfigurationWrapper)
}
public class ParentedBaseWorkflowState<TWorkflow, TParent> : BaseWorkflowState<TWorkflow> where TWorkflow : IWorkflow where TParent : BaseWorkflowState<TWorkflow>
internal sealed record WorkflowContext(BaseStateMachine Machine, string CorrelationId, ITimerService Timers)
{
    [ThreadStatic] public static WorkflowContext? Current;
}
```
- `BaseWorkflowState` reads `WorkflowContext.Current` once in its constructor (throws `InvalidOperationException("Workflow states can only be created by the workflow engine")` if null) and keeps `Machine`, `CorrelationId` and `Timers` in fields declared on the framework base class, so they are excluded from persistence. `CreateDelayedMessageSender<T>()` returns `new DelayedMessageSender<T>(Timers, Guid.NewGuid())`. `Configure` calls `StateHandlerScanner.Configure(..., typeof(ParentedBaseWorkflowState<,>), typeof(BaseWorkflowState<>))`.

- [ ] **Step 1: Write failing tests:** `WorkflowState_WithoutContext_Throws`; `WorkflowState_ExposesNoFireExternal` (reflection: `BaseWorkflowState<>` has no member named `Fire`, `FireExternal`, `GetCurrentStateForExternal`, `SendCurrentStatus` or `SendCurrentStatusAllStateMachines`); `AllStateMachineContainerSetup_SkipsWorkflows` (constructed over the test assembly, its internal setup list contains no `IWorkflow` type).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~WorkflowTypesTest` → compile failure.
- [ ] **Step 3: Implement** the types. Add `&& !typeof(IWorkflow).IsAssignableFrom(t)` to both discovery filters.
- [ ] **Step 4: Run** the filter and the full suite → PASS.
- [ ] **Step 5: Format and commit** — `feat(workflow): public authoring types`.

### Task 5: `WorkflowMetadata` and startup validation

**Files:**
- Create: `StatePipes/Workflow/Internal/WorkflowMetadata.cs`
- Test: `StatePipes.Test/Workflow/WorkflowMetadataTest.cs`; invalid samples in `StatePipes.Test/Workflow/Samples/Invalid/` (one workflow per violation, each in its own namespace)

**Interfaces:**
- Consumes: Tasks 3–4.
- Produces:
```csharp
internal sealed class WorkflowValidationException(string message) : Exception(message);
internal sealed class WorkflowMetadata
{
    public static WorkflowMetadata Build(Type workflowType);              // throws WorkflowValidationException
    public Type WorkflowType { get; }
    public string WorkflowTypeName { get; }                               // Type.FullName
    public IReadOnlyList<Type> StateTypes { get; }
    public Type FirstStateType { get; }
    public IReadOnlySet<string> FinalStateNames { get; }
    public IReadOnlyList<Type> TriggerTypes { get; }                      // concrete BaseWorkflowTriggerCommand<TWorkflow> types in the assembly
    public IReadOnlyDictionary<string, IReadOnlyList<FieldInfo>> PersistedFields { get; }   // key = state Name
    public bool IsDeclared(string stateName, Type triggerType);           // state or any ancestor has a handler
    public string? ParentOf(string stateName);
}
```
- States are the types in the workflow's assembly that derive from `BaseWorkflowState<TWorkflow>`. Persisted fields are those of spec §3.4 rule 2: walk from the concrete type up to (but excluding) the framework bases, taking `BindingFlags.Instance | Public | NonPublic | DeclaredOnly`.

- [ ] **Step 1: Write failing tests.** Each invalid sample expects `WorkflowValidationException` whose message contains the offending type's name:
  - `ConstructorWithParameters_Fails`
  - `NoFirstState_Fails`, `TwoFirstStates_Fails`
  - `NoFinalState_Fails`
  - `BaseTriggerCommandOnWorkflow_Fails`
  - `BaseStateMachineStateOnWorkflow_Fails`
  - `DuplicateStateNames_Fails` (Review Focus 4)
  - `TriggerNotRoundTrippable_Fails`: a trigger with a `Func<int>` property. The round trip is "serialize a `FormatterServices.GetUninitializedObject` instance with `WorkflowJson.Settings` and deserialize", so this test is written now but only runs after Task 6. Mark it `[Ignore("enabled in Task 6")]` here and remove the attribute in Task 6.

  Valid Cart sample:
  - `Cart_Metadata`: first state `CartOpenState`; final states `{CheckedOutState, AbandonedState}`; `PersistedFields["CartOpenState"]` names `_lines` and `_abandon` exactly.
  - `Cart_IsDeclared`: `IsDeclared("CartOpenState", typeof(AddItem))` is `true`; `IsDeclared("PricingState", typeof(AddItem))` is `false`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~WorkflowMetadataTest` → compile failure.
- [ ] **Step 3: Implement** using `StateHandlerScanner.GetHandlers`. Parent relationships come from `ParentedBaseWorkflowState<,>`'s generic arguments.
- [ ] **Step 4: Run** → PASS (except the ignored test).
- [ ] **Step 5: Format and commit** — `feat(workflow): per-type metadata and startup validation`.

---

## Phase C — Serialization

### Task 6: `WorkflowJson` and `WorkflowDocument`

**Files:**
- Create: `StatePipes/Workflow/Internal/WorkflowJson.cs`, `StatePipes/Workflow/Internal/WorkflowDocument.cs`
- Modify: `StatePipes.Test/Workflow/WorkflowMetadataTest.cs` (remove the `[Ignore]`)
- Test: `StatePipes.Test/Workflow/WorkflowDocumentTest.cs`

**Interfaces:**
- Consumes: Task 5 (`PersistedFields`), Task 4 (`WorkflowContext`), Task 2 (`DelayedMessageSender`).
- Produces:
```csharp
internal static class WorkflowJson
{
    public static JsonSerializerSettings CreateSettings(Assembly serviceAssembly);   // contract resolver + binder + sender converter, TypeNameHandling.Auto
}
internal sealed class WorkflowSerializationException(string message, Exception? inner = null) : Exception(message, inner);
internal static class WorkflowDocument
{
    public static string Extract(WorkflowMetadata metadata, IReadOnlyDictionary<string, object> statesByName, JsonSerializerSettings settings);
    public static void Populate(WorkflowMetadata metadata, IReadOnlyDictionary<string, object> statesByName, string documentJson, JsonSerializerSettings settings);
}
```
- Document shape (§5.2): `{"states":{"<StateName>":{"<field>":value}}}`. The sender converter writes `{"TimerId":"<guid>"}`; on read it builds `DelayedMessageSender<T>(WorkflowContext.Current!.Timers, id)` (the generic `T` comes from the field type). The binder allows the service assembly, the `StatePipes` assembly, `System.Private.CoreLib` and `System.Collections`; anything else throws `JsonSerializationException`. `Populate` sets fields with `FieldInfo.SetValue`, including `readonly` ones. Fields missing from the document keep their constructor values. Document entries without a matching field are ignored.

- [ ] **Step 1: Write failing tests** (the tests set `WorkflowContext.Current` and instantiate Cart states directly):
  - `RoundTrip_PrivateFieldsAndBackingFields`: a sample state with a private list, an auto-property and a readonly field → `Extract` then `Populate` into fresh instances → values equal.
  - `Sender_SerializesAsTimerIdOnly`: the JSON for `_abandon` is exactly `{"TimerId":"<guid>"}`; after `Populate`, the field's `TimerId` equals the original.
  - `BaseFields_NotPersisted`: the JSON does not contain `CorrelationId` or `Machine`.
  - `NewFieldKeepsDefault_RemovedFieldIgnored`: populate from a document missing `_lines` and containing `_gone` → `_lines` is still the initializer's empty list; no exception.
  - `Binder_RejectsForeignType_ErrorNamesStateAndField` (Review Focus 5): a sample state's `object _payload` holds a `System.Net.IPAddress` → `Extract` throws `WorkflowSerializationException` whose message contains `"<StateName>._payload"`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~WorkflowDocumentTest` → compile failure.
- [ ] **Step 3: Implement.** Wrap the serialization of each field in a try/catch that rethrows as `WorkflowSerializationException($"{stateName}.{field.Name}: …")`. Add the trigger round-trip check to `WorkflowMetadata.Build`, using `CreateSettings(workflowType.Assembly)`.
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~WorkflowDocumentTest|FullyQualifiedName~WorkflowMetadataTest"` → PASS.
- [ ] **Step 5: Format and commit** — `feat(workflow): field persistence document and JSON settings`.

---

## Phase D — Engine on the in-memory store

### Task 7: Store contract, `InMemoryWorkflowStore`, `PeriodicSchedule`

**Files:**
- Create: `StatePipes/Workflow/Internal/IWorkflowStore.cs`, `StatePipes/Workflow/Internal/InMemoryWorkflowStore.cs`, `StatePipes/Workflow/Internal/PeriodicSchedule.cs`
- Test: `StatePipes.Test/Workflow/InMemoryWorkflowStoreTest.cs`, `StatePipes.Test/Workflow/PeriodicScheduleTest.cs`

**Interfaces:**
- Produces:
```csharp
internal sealed record WorkflowRecord(string WorkflowType, string CorrelationId, string CurrentState, string DocumentJson, long Version);
internal sealed record TimerRecord(Guid TimerId, string WorkflowType, string CorrelationId, TimeSpan? Period, string MessageTypeFullName, string MessageJson);
internal interface IWorkflowUnitOfWork : IDisposable          // Dispose without Commit = rollback
{
    bool TryLock(string workflowType, string correlationId);
    WorkflowRecord? Load(string workflowType, string correlationId);
    void Upsert(string workflowType, string correlationId, string currentState, string documentJson);   // version+1, timestamps
    void DeleteInstanceAndTimers(string workflowType, string correlationId);
    void UpsertTimer(TimerRecord timer, TimeSpan dueTime);    // expiration = store clock now + dueTime
    void DeleteTimer(Guid timerId);
    bool TimerExists(Guid timerId);
    void Commit();
}
internal interface IWorkflowStore
{
    void EnsureSchema();
    IWorkflowUnitOfWork Begin();
    int FireDueTimers(IReadOnlyCollection<string> workflowTypes, int batchSize, Func<TimerRecord, bool> send);   // send returns false = leave row (unresolvable type)
}
internal static class PeriodicSchedule
{
    public static DateTimeOffset NextDue(DateTimeOffset expiration, TimeSpan period, DateTimeOffset now);
}
internal sealed class InMemoryWorkflowStore(TimeProvider timeProvider) : IWorkflowStore
{
    public void SimulateLock(string workflowType, string correlationId);   // held by "another replica" until ReleaseSimulatedLock
    public void ReleaseSimulatedLock(string workflowType, string correlationId);
    public IReadOnlyCollection<WorkflowRecord> Instances { get; }
    public IReadOnlyCollection<TimerRecord> Timers { get; }
}
```
- `PeriodicSchedule.NextDue` returns `expiration + period * max(1, ceil((now - expiration) / period))` (D16).
- `InMemoryWorkflowStore` stages writes in the unit of work and applies them on `Commit`. A unit of work sees its own staged writes (in `Load` and `TimerExists`). `TryLock` returns `false` if the key is simulated-locked or held by another open unit of work.

- [ ] **Step 1: Write failing tests:**
  - `PeriodicScheduleTest.NextDue`, data-driven: expiration 10:00, period 1 min, now 10:10:15 → 10:11; now 09:59 → 10:01; now exactly 10:03 → 10:04.
  - `InMemoryWorkflowStoreTest.Rollback_DiscardsStagedWrites`; `Commit_AppliesAndIncrementsVersion` (a second upsert gives `Version == 2`).
  - `InMemoryWorkflowStoreTest.TryLock_FailsWhileOtherUnitHoldsIt_AndAfterSimulateLock`.
  - `InMemoryWorkflowStoreTest.FireDueTimers`: a one-shot is sent and deleted; a periodic is sent once after 10 minutes overdue and rescheduled to the `NextDue` value; `send` returning `false` leaves the row unchanged; only the listed workflow types are considered.
  - `InMemoryWorkflowStoreTest.DeleteInstanceAndTimers_RemovesOnlyThatInstancesTimers`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~InMemoryWorkflowStoreTest|FullyQualifiedName~PeriodicScheduleTest"` → compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Format and commit** — `feat(workflow): store contract, in-memory store, skip-missed schedule`.

### Task 8: `WorkflowTimerService` and `WorkflowTimerPoller`

**Files:**
- Create: `StatePipes/Workflow/Internal/WorkflowTimerService.cs`, `StatePipes/Workflow/Internal/WorkflowTimerPoller.cs`, `StatePipes/Comms/Internal/CommandDispatchContext.cs`
- Modify: `StatePipes/Comms/Internal/ReceivedCommandMessage.cs`, `StatePipes/Comms/Internal/StatePipesService.cs` (DoWork sets `CommandDispatchContext` around `ExecuteMessage`; new internal `Requeue`), `StatePipes/StateMachine/Test/Internal/TestStatePipesService.cs` (same)
- Test: `StatePipes.Test/Workflow/WorkflowTimerServiceTest.cs`, `StatePipes.Test/Workflow/WorkflowTimerPollerTest.cs`

**Interfaces:**
- Consumes: Task 7.
- Produces:
```csharp
internal class ReceivedCommandMessage(object command, BusConfig replyTo, int retryCount = 0, bool isTimerDelivery = false)
internal static class CommandDispatchContext            // [ThreadStatic] values for the message being dispatched
{
    public static int RetryCount;
    public static bool IsTimerDelivery;
}
internal interface ICommandRequeuer                     // implemented by StatePipesService and TestStatePipesService
{
    void Requeue(object command, BusConfig replyTo, int retryCount, bool isTimerDelivery);   // Queue(new ReceivedCommandMessage(...))
}
internal sealed class WorkflowTimerService(IWorkflowUnitOfWork unitOfWork, string workflowType, string correlationId, JsonSerializerSettings settings) : ITimerService
internal sealed class WorkflowTimerPoller(IWorkflowStore store, IReadOnlyCollection<string> workflowTypes, ICommandRequeuer requeuer,
                                          BusConfig replyTo, JsonSerializerSettings settings, TimeSpan pollInterval) : IDisposable
{
    public int PollOnce();      // returns number fired; used by tests and by the thread loop
    public void Start();        // background thread: PollOnce every pollInterval; on exception log + back off one interval
}
```
- `WorkflowTimerService.StartOneShot` / `StartPeriodic` → `UpsertTimer(new TimerRecord(id, type, cid, period-or-null, message.GetType().FullName!, JsonConvert.SerializeObject(message, settings)), due)`. `Stop` → `DeleteTimer`; `IsEnabled` → `TimerExists`.
- `PollOnce` calls `store.FireDueTimers(types, 100, send)`. `send` resolves the type by `FullName` among the service assembly's types; an unresolvable type is logged once per `TimerId` (a `HashSet<Guid>`) and returns `false`. A resolved message is deserialized and passed to `requeuer.Requeue(msg, replyTo, 0, isTimerDelivery: true)`.

- [ ] **Step 1: Write failing tests:**
  - `WorkflowTimerServiceTest.Start_WritesRow_Stop_Deletes_IsEnabledReflectsUncommitted`.
  - `WorkflowTimerPollerTest.PollOnce_RequeuesDueMessageFlaggedAsTimerDelivery` (FakeItEasy `ICommandRequeuer`; asserts `isTimerDelivery == true`, `retryCount == 0`, and that the message's `CorrelationId` round-tripped).
  - `WorkflowTimerPollerTest.UnresolvableType_LeftInPlace_LoggedOnce` (two `PollOnce` calls → one log entry; the row still exists).
  - `WorkflowTimerPollerTest.StoreThrows_LoopLogsAndKeepsPolling` (started thread; the fake store throws on the first `FireDueTimers` and succeeds on the second → the second call happens; an error is logged).
  - `StatePipesDispatchTest.RetryCountAndTimerFlag_VisibleToHandlerDuringDispatch` (via `TestStatePipesService`: a handler records `CommandDispatchContext.RetryCount` after `Requeue(cmd, cfg, 3, true)`).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~WorkflowTimer|FullyQualifiedName~StatePipesDispatchTest"` → compile failure.
- [ ] **Step 3: Implement.** In both buses' dispatch loops, set `CommandDispatchContext.RetryCount` and `IsTimerDelivery` from the dequeued message before `ExecuteMessage`, and reset them to `0`/`false` afterwards in a `finally`.
- [ ] **Step 4: Run** → PASS; run the full suite → PASS.
- [ ] **Step 5: Format and commit** — `feat(workflow): persisted timer service and poller; dispatch retry context`.

### Task 9: `WorkflowMachineFactory`

**Files:**
- Create: `StatePipes/Workflow/Internal/WorkflowMachineFactory.cs`
- Modify: `StatePipes/StateMachine/Internal/BaseStateMachine.cs` (add the internal constructor below; `OnTransitioned` calls `SendCurrentStatus()` only when `publishStatus`)
- Test: `StatePipes.Test/Workflow/WorkflowMachineFactoryTest.cs`

**Interfaces:**
- Consumes: Tasks 2, 4, 5, 6.
- Produces:
```csharp
internal BaseStateMachine(IStatePipesService bus, InMemoryTimerService timers, string initialStateName, bool publishStatus)
internal sealed class WorkflowMachine
{
    public BaseStateMachine Machine { get; }
    public IReadOnlyDictionary<string, object> StatesByName { get; }
    public string CurrentState => Machine.CurrentState;
}
internal static class WorkflowMachineFactory
{
    // Sets WorkflowContext.Current for the duration of the build; caller keeps it set while firing.
    public static WorkflowMachine CreateNew(WorkflowMetadata md, WorkflowContext ctxTemplate, IStatePipesService bus, InMemoryTimerService timers);  // at InitialState, then fires InitTrigger → first state OnEntry (+MoveToState)
    public static WorkflowMachine Rehydrate(WorkflowMetadata md, WorkflowRecord record, WorkflowContext ctxTemplate, IStatePipesService bus, InMemoryTimerService timers, JsonSerializerSettings settings);
    public static WorkflowMachine CreateForDiagram(WorkflowMetadata md, IStatePipesService bus, InMemoryTimerService timers);  // no store, no init fire
}
```
- The build follows `BaseStateMachineAndFirstStateContainerSetup.ConfigureStateMachine`, without DI. It instantiates each `md.StateTypes` entry with `Activator.CreateInstance(t, nonPublic: true)`, then runs `new StateWorker(machine, state, disableAutomaticMoveToState: false).Configure()` for each, then `machine.ConfigureInitialState(md.FirstStateType, <no init command>)`. `Rehydrate` passes `record.CurrentState` as `initialStateName` and calls `WorkflowDocument.Populate`. It does **not** fire, so `OnEntry` doesn't run. The factory constructs the machine first and then sets `WorkflowContext.Current = ctxTemplate with { Machine = machine }` (`ctxTemplate.Machine` is ignored, so callers may pass `null!`).

- [ ] **Step 1: Write failing tests** with the Cart sample, a `TestStatePipesService` and the in-memory store:
  - `CreateNew_RunsFirstStateOnEntry_AndLandsInFirstState`.
  - `Rehydrate_StartsInPersistedState_DoesNotRunOnEntry` (a sample state counts `OnEntry` calls in a static counter).
  - `Rehydrate_RestoresFields` (`_lines` count equals the persisted count).
  - `MoveToState_ParentReentry_ReachesFirstSubstate` (a sample parented workflow, mirroring the singleton characterization test).
  - `WorkflowTransitions_DoNotPublishStateStatusEvent` (`EventInvocations` has no `StateStatusEvent`).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~WorkflowMachineFactoryTest` → compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS; run `SingletonMachineCharacterizationTest` → PASS.
- [ ] **Step 5: Format and commit** — `feat(workflow): per-command machine factory`.

### Task 10: `WorkflowEngine`

**Files:**
- Create: `StatePipes/Workflow/Internal/WorkflowEngine.cs`
- Test: `StatePipes.Test/Workflow/WorkflowEngineTest.cs`

**Interfaces:**
- Consumes: Tasks 1, 5–9.
- Produces:
```csharp
internal sealed class WorkflowEngineOptions { public int LockedMaxRetries { get; init; } = 10; public TimeSpan LockedRetryDelay { get; init; } = TimeSpan.FromMilliseconds(50); }
internal sealed class WorkflowEngine(WorkflowMetadata md, IWorkflowStore store, IStatePipesService bus, ICommandRequeuer requeuer,
                                     InMemoryTimerService timers, JsonSerializerSettings settings, WorkflowEngineOptions options)
{
    public void Handle(ITrigger command, string correlationId, BusConfig? replyTo, int retryCount, bool isTimerDelivery);
}
```
- This is the §4.3 pipeline, steps 1–7, exactly as written, with two additions from the Review Focus. First, a null or empty `correlationId` is logged as an error and discarded **before** a unit of work begins. Second, when `isTimerDelivery` is true and no row exists, the engine logs `Workflow timer message for a finished or unknown instance discarded` and rolls back; it never creates an instance. The declared check runs against the state the machine is in immediately before the command fires (for a new instance, that is after the init chain). "Final" is checked on `machine.CurrentState` after `Fire` returns. A lock failure calls `timers.ScheduleCallback(options.LockedRetryDelay, () => requeuer.Requeue(command, replyTo!, retryCount + 1, isTimerDelivery))`. At `retryCount >= LockedMaxRetries` it logs `Workflow command not satisfied: workflow locked (workflow=<type>, correlationId=<id>, command=<type>, retries=<n>)`. `WorkflowContext.Current` is cleared in a `finally`.

- [ ] **Step 1: Write failing tests** with the Cart sample, `InMemoryWorkflowStore`, `TestStatePipesService` and a FakeItEasy `ICommandRequeuer`:
  - `NewInstance_DeclaredCommand_Persists` (the row exists in `CartOpenState` with `_lines.Count == 1`).
  - `NewInstance_UndeclaredCommand_NoRow` (`PriceQuoted` to an unknown id).
  - `PermitReentryIfFalse_StillPersistsFieldChanges` (two `AddItem` → 2 lines, `Version == 2`).
  - `ExistingInstance_UndeclaredCommand_RollsBack_VersionUnchanged`.
  - `Exception_RollsBack_AndCommandDiscarded` (a sample state throws in its guard).
  - `FinalState_DeletesInstanceAndTimers` (`AddItem` starts the abandon timer; `Checkout` → `PriceQuoted` → `CheckedOutState`; store `Instances` and `Timers` are empty).
  - `Locked_RequeuesAfterDelayWithIncrementedRetry` (SimulateLock; advance fake time 50 ms; requeuer called with `retryCount == 1`).
  - `Locked_AtMaxRetries_LogsAndDiscards` (`retryCount: 10` → no requeue; the log contains `Workflow command not satisfied: workflow locked`).
  - `EmptyCorrelationId_DiscardedWithError` (Review Focus 1: no unit of work begun, no row).
  - `TimerDelivery_ForUnknownInstance_DoesNotCreate` (Review Focus 2).
  - `UnknownCurrentState_LogsErrorLeavesRowUntouched` (a seeded row with `current_state = "RemovedState"`).
  - `StoreUnavailable_LogsAndDiscards_NoRequeue` (a FakeItEasy `IWorkflowStore` whose `Begin` throws `NpgsqlException`-like `InvalidOperationException` → error logged, requeuer never called).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~WorkflowEngineTest` → compile failure.
- [ ] **Step 3: Implement.** Use a test-visible `ILogger` (the existing `LoggerHolder.Log`) for the log assertions.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Format and commit** — `feat(workflow): command pipeline with locking, outcomes and requeue`.

### Task 11: Container wiring and `WorkflowStoreConfig`

**Files:**
- Create: `StatePipes/Comms/WorkflowStoreConfig.cs`, `StatePipes/Workflow/Internal/BaseWorkflowTriggerCommandHandler.cs`, `StatePipes/Workflow/Internal/AllWorkflowContainerSetup.cs`
- Modify: `StatePipes/Comms/ServiceConfiguration.cs` (optional last ctor param `WorkflowStoreConfig? workflowStore = null`, property `WorkflowStore`), `StatePipes/Comms/Internal/StatePipesServiceContainerSetup.cs` (construct, register and build `AllWorkflowContainerSetup` after the state machine setup)
- Test: `StatePipes.Test/Workflow/AllWorkflowContainerSetupTest.cs`, `StatePipes.Test/Workflow/WorkflowStoreConfigTest.cs`

**Interfaces:**
- Consumes: Tasks 5–10.
- Produces:
```csharp
[method: JsonConstructor]
public class WorkflowStoreConfig(string host, string database, string username, string clientCertFileName,
    int port = 5432, string passwordFileName = "CommonPassword.txt", int lockedMaxRetries = 10,
    TimeSpan? lockedRetryDelay = null /* 50 ms */, TimeSpan? timerPollInterval = null /* 1 s */)
// properties: Host, Port, Database, Username, ClientCertFileName, PasswordFileName, LockedMaxRetries, LockedRetryDelay, TimerPollInterval
internal class BaseWorkflowTriggerCommandHandler<TCommand, TWorkflow>(WorkflowEngineRegistry registry) : IMessageHandler<TCommand>
    where TWorkflow : IWorkflow where TCommand : BaseWorkflowTriggerCommand<TWorkflow>
// HandleMessage → registry.Get(typeof(TWorkflow)).Handle(cmd, cmd.CorrelationId, responseInfo, CommandDispatchContext.RetryCount, CommandDispatchContext.IsTimerDelivery)
internal sealed class WorkflowEngineRegistry { public WorkflowEngine Get(Type workflowType); }
internal sealed class AllWorkflowContainerSetup(Assembly assembly, Func<WorkflowStoreConfig?, IWorkflowStore> storeFactory, WorkflowStoreConfig? config) : IContainerSetup
```
- `Register`: build `WorkflowMetadata` for every `IWorkflow` type (validation runs here). If any exist and `config` is null, throw `WorkflowValidationException("Service declares workflows but ServiceConfiguration.WorkflowStore is not set")`. Register `WorkflowEngineRegistry` and one handler per trigger type.
- `Build`: `store.EnsureSchema()`; create the engines; create and `Start()` one `WorkflowTimerPoller` for all of the service's workflow types, using the bus's own `BusConfig` as `replyTo`.
- With no workflows, it registers nothing and never calls `storeFactory`.
- `StatePipesServiceContainerSetup` passes `cfg => new PostgresWorkflowStore(cfg!)`. Until Task 14 lands, that factory is a `NotImplementedException` placeholder that is reached only by services that declare workflows, so it can't affect existing services.

- [ ] **Step 1: Write failing tests:**
  - `WorkflowStoreConfigTest.Defaults` (5432, `CommonPassword.txt`, 10, 50 ms, 1 s) and `JsonRoundTrip`.
  - `WorkflowStoreConfigTest.ServiceConfiguration_WithoutWorkflowStore_DeserializesOldJson` (JSON without the property → `WorkflowStore == null`).
  - `AllWorkflowContainerSetupTest.NoWorkflows_NeverTouchesStore` (an assembly with no workflows: `StatePipes` itself; the factory throws if called).
  - `AllWorkflowContainerSetupTest.WorkflowsWithoutConfig_Throws`. Workflow discovery takes an optional `Func<Type,bool>` filter, so the test assembly's invalid samples can be excluded and only `CartWorkflow` is used.
  - `AllWorkflowContainerSetupTest.RegistersHandlerPerTrigger_AndRoutesToEngine` (in-memory store; resolve `IMessageHandler<AddItem>`, handle it, assert the row exists).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~AllWorkflowContainerSetupTest|FullyQualifiedName~WorkflowStoreConfigTest"` → compile failure.
- [ ] **Step 3: Implement** (with the `Func<Type,bool>? workflowFilter = null` ctor parameter).
- [ ] **Step 4: Run** → PASS; run the full suite → PASS.
- [ ] **Step 5: Format and commit** — `feat(workflow): container wiring and WorkflowStoreConfig`.

### Task 12: `WorkflowForTest<TWorkflow>` harness and the Cart end-to-end tests

**Files:**
- Create: `StatePipes/StateMachine/Test/WorkflowForTest.cs`
- Test: `StatePipes.Test/Workflow/CartWorkflowTest.cs`

**Interfaces:**
- Consumes: Tasks 7–11.
- Produces (public):
```csharp
public sealed class WorkflowForTest<TWorkflow> : IDisposable where TWorkflow : IWorkflow
{
    public WorkflowForTest(int lockedMaxRetries = 10, TimeSpan? lockedRetryDelay = null);
    public void SendCommand<TCommand>(TCommand command) where TCommand : class, ICommand;   // queued on TestStatePipesService; waits until the bus is idle
    public string? CurrentState(string correlationId);
    public bool Exists(string correlationId);
    public TField? GetField<TState, TField>(string correlationId, string fieldName);   // deserialized from the stored document
    public void AdvanceTime(TimeSpan by);       // FakeTimeProvider.Advance, then poller.PollOnce(), then wait for idle
    public void SimulateLocked(string correlationId);
    public void ReleaseLock(string correlationId);
    public List<ICommand> CommandInvocations { get; }
    public List<IEvent> EventInvocations { get; }
    public void FilterCommand<T>(int skip = 0, int block = int.MaxValue) where T : class, ICommand;
    public TimedBlockOnFilter<ICommand>? TrapCommand<T>(int skip, int timeoutMsec, bool stopProcessingCmdsWhenTriggered = false) where T : class, ICommand;
}
```
- Built from an Autofac container containing `TestStatePipesService`, `InMemoryTimerService` (with the harness's `FakeTimeProvider`), `InMemoryWorkflowStore` and `AllWorkflowContainerSetup` with filter `t => t == typeof(TWorkflow)`. The real poller thread is **not** started; `AdvanceTime` drives `PollOnce`. "Wait for idle" uses `TestStatePipesService.Flush`.

- [ ] **Step 1: Write failing tests** (spec §10.1 snippet as tests):
  - `AddItem_CreatesCartInOpenState`.
  - `AbandonTimer_AfterSevenDays_FinishesAndDeletes` (`AdvanceTime(7 days)` → `Exists` is `false`; `EventInvocations` is empty because `AbandonedState` publishes nothing).
  - `TimerSurvivesRehydration` (`AddItem`, then a second `AddItem` with the timer restarted, then `AdvanceTime(6 days)` → still exists; `AdvanceTime(1 day + 1 s)` → gone).
  - `Checkout_RequestsPrice_ReplyFinishes` (`CommandInvocations` contains `QuotePrice` with `CorrelationId == "cart-1"`; sending `PriceQuoted` → `EventInvocations` contains `CartCheckedOut`; `Exists` is `false`).
  - `GetField_ReadsPersistedLines`.
  - `SimulateLocked_DiscardsAfterMaxRetries` (`lockedMaxRetries: 2`; `AddItem` while locked; advance 200 ms; `Exists` is `false`).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~CartWorkflowTest` → compile failure.
- [ ] **Step 3: Implement the harness.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Format and commit** — `feat(workflow): WorkflowForTest harness`.

---

## Phase E — PostgreSQL

### Task 13: Infrastructure scripts and project templates

(This comes before the store tasks so that the integration tests in Tasks 14–15 have a database.)

**Files:**
- Modify: `StatePipes.ServiceCreatorTool/Resources/DockerInfrastructureStart_ps1.sample`, `DockerInfrastructureStop_ps1.sample`, `DockerStart_ps1.sample`, `Service_csproj.sample:51`
- Test: `StatePipes.Test/ServiceCreatorTool/InfrastructureTemplateTest.cs`

Content is fixed by spec §8.1–§8.4 verbatim: variables, teardown, certificate commands, copy-out list, `pg_hba.conf` lines, `10-statepipes.sql`, the `docker run` arguments and wrapper command, the readiness loop, the reset hint, step 9 import, the summary line, the hosts-file comment block, the stop-script lines, the `DockerStart` copy line, and the `if exist` csproj line.

- [ ] **Step 1: Write failing tests** (read the embedded resources):
  - `InfrastructureStart_ParsesAsPowerShell`: run `pwsh -NoProfile -Command "$e=$null; [System.Management.Automation.Language.Parser]::ParseFile('<path>', [ref]$null, [ref]$e) | Out-Null; $e.Count"` → output is `0`. Use the same check for Stop and DockerStart, with the moniker tokens `@#$…@#$` substituted first, as the existing template tests do.
  - `InfrastructureStart_ContainsPostgresEssentials`: the start script contains `postgres:17`, `hostssl  all  all       0.0.0.0/0 cert`, `ssl_min_protocol_version=TLSv1.3`, `chmod 600`, `postgres-db-data`, `statepipes.postgres-db.client.p12`, `pg_isready`; it contains no `host ` (non-ssl) line in `pg_hba`.
  - `ServiceCsproj_CopiesPostgresCertGuarded`: `Service_csproj.sample` contains `if exist $(ProgramData)\SmallStep\step-ca\statepipes.postgres-db.client.p12 copy`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~InfrastructureTemplateTest` → FAIL.
- [ ] **Step 3: Edit the four templates.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Manual verification** (spec §10.3, steps 1, 3, 4). Generate a sample solution with the ServiceCreatorTool, then run its `DockerInfrastructureStart.ps1` as Administrator. Confirm: `docker exec postgres-db pg_isready` → `accepting connections`; `docker exec postgres-db psql -U postgres -c "show ssl_min_protocol_version"` → `TLSv1.3`; `psql "host=postgres-db dbname=statepipes user=statepipes sslmode=disable"` from the host is refused. Record the output in the commit message body.
- [ ] **Step 6: Format and commit** — `feat(infra): PostgreSQL with client-certificate auth in docker scripts`.

### Task 14: Connection, schema, advisory lock key

**Files:**
- Modify: `StatePipes/StatePipes.csproj` (add `<PackageReference Include="Npgsql" Version="10.0.0" />`; use the latest stable 10.x that supports net10.0, pinned to an exact version)
- Create: `StatePipes/Workflow/Internal/Postgres/PostgresConnectionFactory.cs`, `WorkflowSchema.cs`, `AdvisoryLockKey.cs`
- Test: `StatePipes.Test/Workflow/AdvisoryLockKeyTest.cs` (Unit), `StatePipes.Test/Workflow/PostgresConnectionIntegrationTest.cs` (Integration), `StatePipes.Test/Workflow/PostgresTestConfig.cs`

**Interfaces:**
- Produces:
```csharp
internal static class PostgresConnectionFactory
{
    public static NpgsqlDataSource Create(WorkflowStoreConfig cfg);   // SslMode=VerifyFull; client cert = X509CertificateLoader.LoadPkcs12FromFile(DirHelper.Find(cfg.ClientCertFileName, DirHelper.FileCategory.Certs), password from DirHelper.Find(cfg.PasswordFileName, …))
}
internal static class WorkflowSchema { public const string Ddl = "…§5.1 verbatim…"; public static void Ensure(NpgsqlDataSource ds); }
internal static class AdvisoryLockKey { public static long For(string workflowType, string correlationId); }   // first 8 bytes (little-endian) of SHA-256(UTF-8(workflowType + "\0" + correlationId))
internal static class PostgresTestConfig { public static WorkflowStoreConfig? TryCreate(); }   // null when the client .p12 is missing; host "postgres-db", db/user "statepipes", cert path absolute under %ProgramData%\SmallStep\step-ca\
```
- Client certificate wiring: use `NpgsqlDataSourceBuilder.UseSslClientAuthenticationOptionsCallback`, setting `ClientCertificates` to the loaded certificate. Don't put a password in the connection string.

- [ ] **Step 1: Write failing tests:**
  - `AdvisoryLockKeyTest.IsStableAcrossProcesses` (Review Focus 3): `For("Samples.CartWorkflow", "cart-1")` equals the literal computed once in this step with a throwaway console snippet and pasted into the test. A second test asserts that `("a","bc")` and `("ab","c")` differ.
  - `PostgresConnectionIntegrationTest.ConnectsWithClientCert_VerifyFull` (`select current_user` → `statepipes`).
  - `PostgresConnectionIntegrationTest.PasswordLoginRefused` (no client cert, `Password=StatePipes123$` → `PostgresException` or an authentication failure).
  - `PostgresConnectionIntegrationTest.NonTlsRefused` (`SslMode=Disable` → refused).
  - `PostgresConnectionIntegrationTest.EnsureSchema_IsIdempotent` (run it twice; both tables exist in the `statepipes` schema).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~AdvisoryLockKeyTest|FullyQualifiedName~PostgresConnectionIntegrationTest"` → compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS (integration tests are PASS with the Task 13 infrastructure up, Inconclusive without it).
- [ ] **Step 5: Format and commit** — `feat(workflow): PostgreSQL connection over client certificate, schema, lock key`.

### Task 15: `PostgresWorkflowStore`

**Files:**
- Create: `StatePipes/Workflow/Internal/Postgres/PostgresWorkflowStore.cs`
- Modify: `StatePipes/Comms/Internal/StatePipesServiceContainerSetup.cs` (replace the Task 11 placeholder factory)
- Test: `StatePipes.Test/Workflow/PostgresWorkflowStoreIntegrationTest.cs`

**Interfaces:**
- Consumes: Tasks 7, 14.
- Produces: `internal sealed class PostgresWorkflowStore(WorkflowStoreConfig cfg) : IWorkflowStore, IDisposable`, implementing the Task 7 contract against §5.1. Specifics:
  - `TryLock` → `SELECT pg_try_advisory_xact_lock(@key)` with `AdvisoryLockKey.For`.
  - `Upsert` → `INSERT … ON CONFLICT (workflow_type, correlation_id) DO UPDATE SET current_state, document, version = workflow_instance.version + 1, updated_at = now()` (inserted with `version = 1`, `created_at = updated_at = now()`).
  - `UpsertTimer` → `expiration_time = now() + @due`, conflict on `timer_id`.
  - `FireDueTimers` uses its own transaction: `SELECT … WHERE workflow_type = ANY(@types) AND expiration_time <= now() ORDER BY expiration_time LIMIT @batch FOR UPDATE SKIP LOCKED`. Then for each row, `send`. A one-shot row is deleted. A periodic row is updated with `SET expiration_time = expiration_time + period * GREATEST(1, CEIL(EXTRACT(EPOCH FROM (now() - expiration_time)) / EXTRACT(EPOCH FROM period)))`. Then commit.
- Each test uses a unique `workflow_type` (`$"it.{Guid.NewGuid():N}"`) and deletes its rows in cleanup.

- [ ] **Step 1: Write failing integration tests:**
  - `RoundTrip_UpsertLoad_VersionIncrements`.
  - `Rollback_LeavesNoRow`.
  - `AdvisoryLock_ContendedAcrossTwoUnits` (unit A locks; unit B's `TryLock` is `false`; after A commits, B's retry is `true`).
  - `AdvisoryLock_WorksBeforeRowExists` (the creation race).
  - `DeleteInstanceAndTimers_RemovesBoth`.
  - `FireDueTimers_TwoConcurrentPollers_EachTimerOnce`: insert 50 due one-shots, run two `FireDueTimers` on two threads concurrently, and the union of sent `TimerId`s has 50 distinct entries with no duplicates.
  - `FireDueTimers_PeriodicOverdue_SkipsMissed`: a periodic timer with a 1 s period and expiration set 10.5 s in the past via a direct `UPDATE` → sent once; the new expiration is in the future and less than 1 s ahead.
  - `FireDueTimers_SendFalse_LeavesRow`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~PostgresWorkflowStoreIntegrationTest` → compile failure.
- [ ] **Step 3: Implement**, and wire `cfg => new PostgresWorkflowStore(cfg!)` into `StatePipesServiceContainerSetup`.
- [ ] **Step 4: Run** → PASS (infrastructure up); run the full suite → PASS.
- [ ] **Step 5: Format and commit** — `feat(workflow): PostgreSQL workflow store`.

---

## Phase F — Diagrams

### Task 16: Diagrammer workflow path and final-state styling

**Files:**
- Modify: `StatePipes/StateMachine/Internal/AllStateMachineDummyContainerSetup.cs` (for each `IWorkflow` type, `WorkflowMetadata.Build` + `WorkflowMachineFactory.CreateForDiagram`, then register the resulting `BaseStateMachine` with `StateMachineManager` under the workflow type; diagrammer only), `StatePipes/StateMachine/Internal/BaseStateMachine.cs` (`internal IReadOnlySet<string> FinalStates { get; set; } = new HashSet<string>()` passed to the enhancer), `StatePipes/StateMachine/Internal/DotGraphEnhancer.cs` (nodes in `FinalStates` get `peripheries=2`)
- Test: `StatePipes.Test/Workflow/WorkflowDiagramTest.cs`

- [ ] **Step 1: Write failing tests:**
  - `CartDiagram_ContainsAllStatesAndTransitions` (`GetDotGraph()` contains `CartOpenState`, `PricingState`, `CheckedOutState`, `AbandonedState`, and the `Checkout` and `PriceQuoted` edges).
  - `CartDiagram_FinalStatesDoubleBordered` (the `CheckedOutState` node attributes contain `peripheries=2`).
  - `CartDiagram_ListsPublishedEvents` (contains `CartCheckedOut`).
  - `RuntimeStateMachineManager_HasNoWorkflows` (`AllStateMachineContainerSetup` over the test assembly: `GetAllStateMachines()` contains no workflow).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~WorkflowDiagramTest` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Manual check:** run `StatePipes.Diagrammer` against the test assembly output (see `docs/VisualStudioExternalTools.md` for its arguments) and open the generated `CartWorkflow` PDF. The final states have double borders.
- [ ] **Step 6: Format and commit** — `feat(diagrammer): diagram workflows; mark final states`.

---

## Phase G — ServiceCreatorTool

### Task 17: `-w` Add Workflow

**Files:**
- Create: `StatePipes.ServiceCreatorTool/WorkflowGeneratorTool.cs`; resources `Workflow_cs.sample`, `WorkflowTopLevelState_cs.sample`, `WorkflowFinishedState_cs.sample`, `DefaultServiceConfiguration_AddWorkflowStore.sample` (all as embedded resources, matching how the csproj includes the existing samples)
- Modify: `StatePipes.ServiceCreatorTool/Program.cs:14-55,191-194` (`-w` flag → `WorkflowGeneratorTool.CreateNewWorkflow`), `StatePipes.ServiceCreatorTool/ToolArguments.cs:8-10` (add `--workflow-name`), `StatePipes.ServiceCreatorTool/Resources/DefaultServiceConfiguration_cs.sample` (declare `WorkflowStoreConfig? workflowStore = null; // Workflow store injection point, Do Not Delete or Modify` before `Type setup`, and pass `workflowStore` as the last `ServiceConfiguration` argument)
- Test: `StatePipes.Test/ServiceCreatorTool/WorkflowGeneratorToolTest.cs`

**Interfaces:**
- Produces: `public static ToolResult WorkflowGeneratorTool.CreateNewWorkflow(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, IToolInput input)`, with the same shape as `StateMachineGeneratorTool.CreateNewStateMachine`.
- Generated marker: `public interface <Name> : IWorkflow { }`, mirroring `StateMachine_cs.sample`. `WorkflowTopLevelState` is `TopLevelState_cs.sample` with `BaseWorkflowState<…>` and its injection points 5–9 unchanged. `WorkflowFinishedState` is `internal class FinishedState : BaseWorkflowState<…>, IFinalWorkflowState { public FinishedState() { } }`.
- `DefaultServiceConfiguration.cs` insertion, idempotent: if `new WorkflowStoreConfig(` is already present → skip. Else, if the injection-point line is present → replace it with `_AddWorkflowStore.sample`'s content: `WorkflowStoreConfig? workflowStore = new WorkflowStoreConfig("postgres-db", "statepipes", "statepipes", "statepipes.postgres-db.client.p12");`. Else, if the anchor `Type setup = typeof(DefaultSetup)!;` is present → insert that line before it and replace `new ServiceArgs(null))` with `new ServiceArgs(null), workflowStore)`. Else → `ToolResult.Failed` with a message naming the file.
- `.Service.csproj` insertion, idempotent: skip if it already contains `statepipes.postgres-db.client.p12`. Otherwise append `&#xD;&#xA;` + the §8.4 line just before the closing `" />` of the PostBuild `Exec Command`. If there is no PostBuild `Exec`, fail with a message.

- [ ] **Step 1: Write failing tests** using `ToolTestHelpers` (the same pattern as `CreateOperationsTest.StateMachine_Headless_CreatesFiles`):
  - `Workflow_Headless_CreatesMarkerTopLevelAndFinished`.
  - `Workflow_Existing_RefusesAndWritesNothing`.
  - `Workflow_InsertsStoreConfig_NewTemplate`.
  - `Workflow_InsertsStoreConfig_LegacyFileViaAnchor`.
  - `Workflow_SecondWorkflow_InsertionsIdempotent` (exactly one `new WorkflowStoreConfig(` and one `.p12` copy line after two runs).
  - `Workflow_GeneratedSolution_Builds` (extend the existing `HeadlessEndToEndTest` flow: new solution → `-w Cart` → `dotnet build` succeeds; categorize it the same way as the existing end-to-end test).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~WorkflowGeneratorToolTest` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Format and commit** — `feat(tool): -w Add Workflow`.

### Task 18: Workflow-aware `-a`, `-t`, `-pti`, and `CLAUDE.md`

**Files:**
- Create resources: `UnparentedWorkflowState_cs.sample`, `ParentedWorkflowState_cs.sample`, `FirstParentedWorkflowState_cs.sample`, `WorkflowTrigger_cs.sample`, `WorkflowPeriodicOnEntry_cs.sample`
- Modify: `StatePipes.ServiceCreatorTool/StateGeneratorTool.cs` (choose workflow templates; `--final` adds `, IFinalWorkflowState`; interactive prompt "Is this a final state?"), `TriggerGeneratorTool.cs`, `PeriodicTriggerGeneratorTool.cs`, `StateMachineTypesHelper.cs` (add `public bool IsWorkflow(string machineName)`, which is true when `StateMachines/<Name>/<Name>.cs` contains `: IWorkflow`), `ToolArguments.cs` (`--final`), `ClaudeMdWriter.cs` (a "Workflows" section summarizing spec §3.4 rules 1–7 as one bullet each)
- Test: `StatePipes.Test/ServiceCreatorTool/WorkflowStateTriggerToolTest.cs`, plus a `ClaudeMdWriterTest` addition

- [ ] **Step 1: Write failing tests:**
  - `State_OnWorkflow_UsesBaseWorkflowState`.
  - `State_Final_AddsIFinalWorkflowState`.
  - `State_OnSingleton_Unchanged` (the generated content is byte-identical to before).
  - `Trigger_OnWorkflow_DerivesBaseWorkflowTriggerCommand`.
  - `Periodic_OnWorkflow_StampsCorrelationId` (the injected `OnEntry` contains `CorrelationId = CorrelationId`).
  - `ClaudeMd_ContainsWorkflowsSection` (contains `## Workflows` and `parameterless constructor`).
  - Extend `Workflow_GeneratedSolution_Builds`: add a final state, a trigger and a periodic trigger to the generated workflow; `dotnet build` still succeeds.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~WorkflowStateTriggerToolTest|FullyQualifiedName~ClaudeMdWriterTest"` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS; run the full `ServiceCreatorTool` test folder → PASS.
- [ ] **Step 5: Format and commit** — `feat(tool): workflow variants for state, trigger, periodic; CLAUDE.md workflows`.

---

## Phase H — Claude plugin

### Task 19: Skill content and scaffold operations

**Files:**
- Modify: `StatePipes.ClaudePlugin/skills/statepipes-domain-fit/SKILL.md`, `statepipes-domain-modeling/SKILL.md`, `statepipes-conventions/SKILL.md`, `statepipes-scaffold/SKILL.md`, `statepipes-scaffold/operations/add-state.md`, `add-trigger.md`, `add-periodic.md`, `new-service.md`, `StatePipes.ClaudePlugin/tests/scenarios.md`
- Create: `StatePipes.ClaudePlugin/skills/statepipes-scaffold/operations/add-workflow.md`
- Test: the existing `StatePipes.Test/ClaudePlugin/ScaffoldOperationGoldenTest.cs`, `RecipeTemplateCheckTest.cs` and `PluginGuidanceTest.cs` (extend them with workflow cases)

The content follows spec §9.3's table row by row. `add-workflow.md` gives the ServiceCreatorTool command (`-w -headless --workflow-name <Name>`) and both §9.1 insertions, with their anchors and idempotency rules, for agents that edit files by hand.

- [ ] **Step 1: Write failing tests:**
  - `ScaffoldOperationGoldenTest` gains an `add-workflow` case. It applies the recipe to a fixture service and compares with the output of the ServiceCreatorTool from Task 17 (the existing golden pattern).
  - `PluginGuidanceTest.ConventionsCoverWorkflowRules`: `statepipes-conventions/SKILL.md` contains `parameterless constructor`, `IFinalWorkflowState`, `CorrelationId`, `[IgnoreIf]` and `FireExternal`.
  - `RecipeTemplateCheckTest` picks up the new operation automatically; if it doesn't, register it.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ClaudePlugin` → FAIL.
- [ ] **Step 3: Write the content.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Format and commit** — `docs(plugin): workflow modeling, conventions and scaffold operations`.

### Task 20: `Check-StatePipesService.ps1` workflow checks

**Files:**
- Modify: `StatePipes.ClaudePlugin/skills/statepipes-verify/scripts/Check-StatePipesService.ps1`, `StatePipes.ClaudePlugin/skills/statepipes-verify/SKILL.md`
- Create fixtures: `StatePipes.Test/ClaudePlugin/Fixtures/WorkflowGood/…`, `WorkflowBad/…` (minimal `.cs` trees)
- Test: `StatePipes.Test/ClaudePlugin/VerifyScriptWorkflowTest.cs`

These are text checks over `StateMachines/**`, following the script's existing rule style (the `# Rule:` comment headers). For each workflow folder (the marker file contains `: IWorkflow`):
- Rule `workflow-ctor`: a class deriving `BaseWorkflowState<` / `ParentedBaseWorkflowState<` declares a constructor with parameters.
- Rule `workflow-final`: no file in the folder contains `IFinalWorkflowState`.
- Rule `workflow-fireexternal`: a workflow state file contains `FireExternal`.
- Rule `workflow-test`: there is no test file in the solution's `*.Test` project referencing `WorkflowForTest<<Name>>`.

- [ ] **Step 1: Write failing tests:** run `pwsh -NoProfile -File Check-StatePipesService.ps1 -Path <fixture>` (the script's existing parameters). For `WorkflowGood`, the exit code is 0 and there are no `workflow-` findings. For `WorkflowBad`, the output contains each of the four rule ids exactly once.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~VerifyScriptWorkflowTest` → FAIL.
- [ ] **Step 3: Implement the rules**, and list them in `statepipes-verify/SKILL.md`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Format and commit** — `feat(plugin): verify script checks workflows`.

---

## Phase I — Final verification

### Task 21: Whole-branch verification

- [ ] **Step 1:** `dotnet format --verify-no-changes` → exit code 0.
- [ ] **Step 2:** `dotnet build` → 0 errors and no new warnings in `StatePipes`.
- [ ] **Step 3:** With the Task 13 infrastructure up: `dotnet test --settings Tests.runsettings` → all PASS, no Inconclusive in `Workflow/`.
- [ ] **Step 4:** Rule 1 sweep: `git diff main --name-only -- '*.cs' | xargs grep -nE '\.Result\b|\.Wait\(\)'` → no hits in workflow or timer code.
- [ ] **Step 5:** Spec §10.3, step 5: re-run `DockerInfrastructureStart.ps1`; rows created by `PostgresWorkflowStoreIntegrationTest` (skip its cleanup once with `STATEPIPES_KEEP_IT_ROWS=1`) still exist afterwards.
- [ ] **Step 6:** Check conformance with `docs/Paper.md` (Rule 2) against spec §11. The Cart diagram (Task 16) shows every state and transition, and adding a state needed no edits to other states (confirmed by `AbandonedState` in the samples).
- [ ] **Step 7:** Commit any fixes — `chore(workflow): final verification fixes`.
