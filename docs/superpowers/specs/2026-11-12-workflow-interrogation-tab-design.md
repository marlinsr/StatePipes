# Workflow Interrogation Tab — Design

**Date:** 2026-11-12
**Status:** Approved design, pending implementation plan
**Scope:** `StatePipes` core (messages, workflow engine/registry, handler), `StatePipes.Explorer` (new tab), `StatePipes.Test`

---

## 1. Intent

Operators and developers need to see where a specific workflow instance is. A new **WORKFLOW** tab in StatePipes
Explorer accepts a `correlationId` and an **INTERROGATE** button. Pressing it asks the connected service for every
running workflow instance with that `CorrelationId` and renders, for each one, the workflow's state machine diagram
(Graphviz, like the STATE MACHINE tab) with the instance's current state highlighted, a header with workflow type,
current state and version, and the instance's persisted document JSON.

### Confirmed assumptions

- Scope is the **single service** the Explorer instance is connected to (not every service on the broker).
- "Correlates" means **exact match** on `CorrelationId`, across every workflow type in that service.
- **One-shot** request per button press; no periodic polling of diagrams.
- Display per instance: diagram with current state highlighted + type/state/version header + document JSON (option C).

### Out of scope

- **Finished workflows.** On reaching a final state, `WorkflowEngine` calls `DeleteInstanceAndTimers`; the row is gone.
  Only running instances can be interrogated. Retaining history is a separate feature.
- Cross-service interrogation, partial/prefix correlationId matching, live refresh.
- Changes to `GetAllStateMachineDiagramsCommand` / `StateMachineDiagramsEvent`.

### Consistency with the persisted-workflows design

`2026-11-08-persisted-workflows-design.md` excludes workflows from `GetAllStateMachineDiagramsCommand`,
`GetAllStateMachineStatusCommand` and `StateStatusEvent` to avoid flooding status consumers. This feature keeps that
exclusion: it is an on-demand, per-correlationId, read-only query and publishes nothing.

---

## 2. Approach

A new dedicated query command / response event pair (chosen over overloading `GetAllStateMachineDiagramsCommand`,
whose `List<string>` payload has no room for type/version/document, and over a two-call list-then-fetch protocol,
which is unnecessary since a correlationId matches at most one instance per workflow type).

---

## 3. Message contract

New public types in `StatePipes/Messages` (same namespace as the existing diagram messages, so Explorer can
deserialize them with no extra type loading):

```csharp
public class GetWorkflowDiagramsCommand(string correlationId) : ICommand
{
    public string CorrelationId { get; } = correlationId;
}

public class WorkflowDiagramsEvent(string correlationId, IReadOnlyList<WorkflowInstanceDiagram> workflows) : IEvent
{
    public string CorrelationId { get; } = correlationId;
    public IReadOnlyList<WorkflowInstanceDiagram> Workflows { get; } = workflows;
}

public class WorkflowInstanceDiagram(string workflowType, string currentState, long version,
                                     string dotGraph, string documentJson, string? error)
{
    public string WorkflowType { get; } = workflowType;   // workflow type full name
    public string CurrentState { get; } = currentState;
    public long Version { get; } = version;
    public string DotGraph { get; } = dotGraph;           // empty when the diagram could not be built
    public string DocumentJson { get; } = documentJson;   // empty when the row could not be loaded
    public string? Error { get; } = error;                // null on success
}
```

The response echoes `CorrelationId` so the Explorer can ignore replies to earlier requests.

---

## 4. Service side

### 4.1 `WorkflowMachineFactory.CreateForDiagram` overload

`CreateForDiagram(WorkflowMetadata md, IStatePipesService bus, InMemoryTimerService timers, string currentState)`
calls the existing private `Build(...)` with `currentState` as the initial state, clearing `WorkflowContext.Current`
in a `finally` exactly like the existing overload. `Build` configures states and the initial state only — no triggers
fire and no `OnEntry` runs, so building is side-effect free. The existing overload delegates to the new one with
`typeof(InitialState).Name`.

### 4.2 `WorkflowEngine.Interrogate(string correlationId)`

Returns `WorkflowInstanceDiagram?`:

1. `using var uow = store.Begin();` → `var record = uow.Load(_type, correlationId);` — **no `TryLock`**, never
   `Commit()`; disposal rolls back. Interrogation never blocks or delays in-flight commands.
2. `record == null` → return `null`.
3. If `record.CurrentState` is not in `md.StateTypes` → build the diagram via the existing
   `CreateForDiagram(md, bus, timers)` (initial state) and return an entry with
   `Error = $"Unknown state {record.CurrentState}"`, keeping `CurrentState`, `Version`, `DocumentJson` from the row.
4. Otherwise → `CreateForDiagram(md, bus, timers, record.CurrentState).Machine.GetDotGraph()`; `DotGraphEnhancer`
   fills the current state and double-borders final states. Return the entry with `Error = null`.

Any exception inside steps 1–4 is caught, logged via `Log?.LogException`, and returned as an entry with
`WorkflowType = _type`, `Error = ex.Message`, empty `DotGraph`/`DocumentJson`, empty `CurrentState`, `Version = 0`.

### 4.3 `WorkflowEngineRegistry.Interrogate(string correlationId)`

Calls `Interrogate` on every engine, drops nulls, orders by `WorkflowType` (ordinal), returns
`IReadOnlyList<WorkflowInstanceDiagram>`.

### 4.4 `GetWorkflowDiagramsCommandHandler`

`internal class GetWorkflowDiagramsCommandHandler(IStatePipesService bus, IComponentContext context)
: IMessageHandler<GetWorkflowDiagramsCommand>`

- `responseInfo == null` → return (matches `GetAllStateMachineDiagramsCommandHandler`).
- `context.ResolveOptional<WorkflowEngineRegistry>()` — the registry is only registered when the service declares
  workflows.
- No registry, or blank `CorrelationId` → reply with an empty list.
- Otherwise reply with `registry.Interrogate(command.CorrelationId)`.
- Reply via `bus.SendResponse(new WorkflowDiagramsEvent(command.CorrelationId, list), responseInfo)`.

Registered in `BaseStateMachineAndFirstStateContainerSetup.RegisterOnce` next to
`GetAllStateMachineDiagramsCommandHandler`, so it is present in every service. (If `IComponentContext` resolution
proves awkward with the existing handler-resolution path, the fallback is to register `WorkflowEngineRegistry`
unconditionally as an empty instance; the observable behavior is identical.)

Store calls are synchronous, as in the existing `WorkflowEngine.Handle`; no `.Result`/`.Wait()` is introduced.

---

## 5. Explorer

### 5.1 Wiring

- `TabEnum` gains `Workflow`.
- `Home.razor` gains `<Tab Title="WORKFLOW" OnClick="@(() => ToggleDisplayMode(TabEnum.Workflow))">` after
  STATE MACHINE, hosting `<WorkflowDiagramViewer InstanceGuid=@InstanceGuid TabId=@_showTab>`.
- New component `Components/Pages/WorkflowDiagramViewer.razor` / `.razor.cs` / `.razor.css`, following the
  `StateMachineDiagramViewer` pattern and reusing `/diagrams.js` (`renderDot`). No new JS.

### 5.2 Layout

```
[ correlationId ____________________ ] [ INTERROGATE ]
status: "Waiting for reply…" | "2 workflows found" | "No running workflows for 'abc'" | "No reply after 10 s"

── CartWorkflow · state PricingState · v7 ───────────────
   [ graphviz diagram, div id WorkflowDiagram0, 500px ]
   ▸ Document   (CollapsableDivision, collapsed by default)
       <pre> pretty-printed DocumentJson </pre>
   (red error line when Error is set)
── NextWorkflow · ... ─────────────────────────────────
```

The header shows the short type name (after the last `.`); the full name is the element's tooltip.

### 5.3 Behavior

- Button disabled when the input is blank, a request is pending, or the Explorer is not connected to the broker.
  Enter in the input triggers interrogation. Input is trimmed.
- On press: record `_pendingCorrelationId`, the send time, and a reference to the current `WorkflowDiagramsEvent`
  payload (`EventEntry.Obj`, if any) from `GetEventJsons`; then
  `_statePipesHandler.SendCommand(InstanceGuid, typeof(GetWorkflowDiagramsCommand).FullName!, json)`.
- While pending, a 250 ms timer reads `GetEventJsons(InstanceGuid)` and accepts the `WorkflowDiagramsEvent` payload only
  if it is a **different object** from the snapshot payload **and** its `CorrelationId` equals the pending one. Explorer's
  `EventJsonRepository` keeps one `EventEntry` per type and replaces its `Obj` in place, so freshness is judged by the
  payload object (a new deserialized instance per received event), not the entry. This rejects both replies to older
  correlationIds and the stale prior reply when re-interrogating the same id. All session state is mutated on the Blazor
  renderer context (the timer only marshals via `InvokeAsync`); a new request clears the previous result.
- On match: stop the timer, store the result, `InvokeAsync(StateHasChanged)`, then `renderDot` each non-empty
  `DotGraph` into `WorkflowDiagram{i}`.
- After 10 s without a match: status "No reply after 10 s", clear pending, re-enable the button.
- The last result persists across tab switches; diagrams are redrawn when the tab becomes active again (driven by
  `TabId`, as in the existing viewer).
- `DisposeAsync` disposes the timer and the JS module, as in the existing viewer.

### 5.4 Testable helper

Pure logic lives in `StatePipes.Explorer/NonWebClasses/WorkflowInterrogation.cs`:

- `bool IsMatchingReply(IEvent? candidate, IEvent? snapshot, string pendingCorrelationId)` (payload objects)
- `string PrettyJson(string json)` — `JToken.Parse(json).ToString(Formatting.Indented)`; returns the input unchanged
  if parsing fails.
- `string ShortTypeName(string fullName)`

---

## 6. Error handling

| Case | Result |
|---|---|
| Service has no workflows | Empty list → "No running workflows for 'X'" |
| No instance with that id (never existed, or finished and deleted) | Empty list → same message |
| Persisted state not a known state | Entry with initial-state diagram + red "Unknown state X" |
| Store throws for one workflow type | Logged; entry with `WorkflowType` + `Error`; other types still returned |
| Command without `responseInfo` | Ignored |
| No reply | Explorer gives up after 10 s |
| `renderDot` JS failure | Logged, as in `StateMachineDiagramViewer` |

---

## 7. Testing

MSTest in `StatePipes.Test/Workflow/`, using the `CartWorkflow` sample and `InMemoryWorkflowStore`:

1. `WorkflowEngine.Interrogate` returns `null` for an unknown id.
2. After driving a cart into `PricingState`: returns `CurrentState == PricingState`, the stored `Version`,
   the stored `DocumentJson`, and a DOT graph in which `PricingState` is `style=filled`.
3. Interrogating does not lock or mutate: `Version` unchanged afterwards, and a subsequent command succeeds.
4. A hand-written row with an unknown state yields a non-empty diagram and `Error` containing the state name.
5. `WorkflowEngineRegistry.Interrogate` with two workflow types sharing an id returns both, ordered by type name.
6. `GetWorkflowDiagramsCommandHandler` (captured via `TestStatePipesService`): sends `WorkflowDiagramsEvent` echoing
   the correlationId; empty list when no registry is registered; sends nothing when `responseInfo` is null.
7. `WorkflowDiagramsEvent` round-trips through `JsonUtility` (serialize → `CloneToType`) with all fields intact.
8. `WorkflowInterrogation` helper: matching/stale/other-id cases for `IsMatchingReply`; valid and invalid JSON for
   `PrettyJson`.

Explorer UI is verified manually by running Explorer against a sample service with a running workflow (no Razor
component test harness exists in the repo).

Completion per CLAUDE.md: `dotnet format`, `dotnet build`, `dotnet test --settings tests.runsettings`.

---

## 8. Architectural integrity (docs/Paper.md)

The feature is a read-only query. It adds no states, triggers or transitions, does not alter `OnEntry`/`OnExit`
lifecycles, and builds diagrams from the same attribute declarations (B5 diagram accuracy preserved). It is
consistent with Paper.md's description of Explorer as a live view with highlighted diagrams.
