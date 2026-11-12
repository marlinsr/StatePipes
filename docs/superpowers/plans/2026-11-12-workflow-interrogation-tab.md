# Workflow Interrogation Tab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A WORKFLOW tab in StatePipes Explorer that takes a correlationId, sends one query to the connected service, and renders a highlighted Graphviz diagram, header and document JSON for every running workflow instance with that id.

**Architecture:** New public `GetWorkflowDiagramsCommand` / `WorkflowDiagramsEvent` pair. An always-registered handler asks `WorkflowEngineRegistry` → each `WorkflowEngine.Interrogate`, which loads the row without locking and builds a side-effect-free diagram machine at the persisted state. Explorer gets a new viewer component modelled on `StateMachineDiagramViewer`, with its pure logic in a unit-tested helper.

**Tech Stack:** .NET (net8.0/net10.0), Autofac, Stateless, Newtonsoft.Json, Blazor Server, d3-graphviz (`wwwroot/diagrams.js`), MSTest + Moq.

**Spec:** `docs/superpowers/specs/2026-11-12-workflow-interrogation-tab-design.md`

## Global Constraints

- No `.Result` / `.Wait()` anywhere (CLAUDE.md Rule 1).
- Run `dotnet format` after code changes, before completing each task (CLAUDE.md Rule 3).
- Test command: `dotnet test StatePipes.Test --settings Tests.runsettings --filter "FullyQualifiedName~<Class>"`.
- New messages live in namespace `StatePipes.Messages`, folder `StatePipes/Messages/`.
- Interrogation never calls `TryLock` and never calls `Commit()`.
- Existing `GetAllStateMachineDiagramsCommand` / `StateMachineDiagramsEvent` are not modified.
- Explorer reply timeout: 10 s; reply poll interval: 250 ms; diagram div ids `WorkflowDiagram{i}`; div height 500px.
- Status strings (exact): `Waiting for reply…`, `{n} workflows found` (`1 workflow found` when n == 1), `No running workflows for '{id}'`, `No reply after 10 s`.

## Review Focus

1. Interrogating must not run any state's `OnEntry` (e.g. `PricingState.OnEntry` sends `QuotePrice`; `CountFirstState.OnEntry` bumps `Entries`) → Task 2 test `Interrogate_DoesNotRunOnEntryOrSendCommands`.
2. Interrogating while a command holds the instance's lock must still answer, not block or skip → Task 2 test `Interrogate_WhileLocked_StillReturnsInstance`.
3. Re-interrogating the same id must not accept the previous reply still sitting in the event list → Task 5 test `IsMatchingReply_SameInstanceAsSnapshot_False`.
4. A correlationId typed with surrounding whitespace, or only whitespace, must be trimmed / rejected before sending → Task 5 tests `NormalizeInput_*`.
5. A store failure for one workflow type must not hide the others → Task 3 test `Registry_OneEngineThrows_OtherStillReturned`.

---

## File Map

| File | Responsibility |
|---|---|
| Create `StatePipes/Messages/GetWorkflowDiagramsCommand.cs` | Query command |
| Create `StatePipes/Messages/WorkflowDiagramsEvent.cs` | Reply event |
| Create `StatePipes/Messages/WorkflowInstanceDiagram.cs` | One instance in the reply |
| Modify `StatePipes/Workflow/Internal/WorkflowMachineFactory.cs` | `CreateForDiagram` overload with `currentState` |
| Modify `StatePipes/Workflow/Internal/WorkflowEngine.cs` | `Interrogate(correlationId)` |
| Modify `StatePipes/Workflow/Internal/BaseWorkflowTriggerCommandHandler.cs` | `WorkflowEngineRegistry.Interrogate(correlationId)` |
| Create `StatePipes/Workflow/Internal/GetWorkflowDiagramsCommandHandler.cs` | Handler |
| Modify `StatePipes/StateMachine/Internal/BaseStateMachineAndFirstStateContainerSetup.cs` (`RegisterOnce`) | Register handler |
| Create `StatePipes.Explorer/NonWebClasses/WorkflowInterrogation.cs` | Pure Explorer logic |
| Create `StatePipes.Explorer/Components/Pages/WorkflowDiagramViewer.razor` / `.razor.cs` / `.razor.css` | Tab UI |
| Modify `StatePipes.Explorer/Components/Pages/TabEnum.cs`, `Home.razor` | Tab wiring |
| Create tests: `StatePipes.Test/Workflow/WorkflowDiagramsEventTest.cs`, `WorkflowInterrogateTest.cs`, `GetWorkflowDiagramsCommandHandlerTest.cs`, `StatePipes.Test/Explorer/WorkflowInterrogationTest.cs` | Tests |

---

### Task 1: Message contract

**Files:**
- Create: `StatePipes/Messages/GetWorkflowDiagramsCommand.cs`, `StatePipes/Messages/WorkflowDiagramsEvent.cs`, `StatePipes/Messages/WorkflowInstanceDiagram.cs`
- Test: `StatePipes.Test/Workflow/WorkflowDiagramsEventTest.cs`

**Interfaces:**
- Produces (all `public`, namespace `StatePipes.Messages`, primary-constructor + get-only properties, same style as `StateMachineDiagramsEvent`):
  - `GetWorkflowDiagramsCommand(string correlationId) : ICommand` — `string CorrelationId`
  - `WorkflowDiagramsEvent(string correlationId, IReadOnlyList<WorkflowInstanceDiagram> workflows) : IEvent` — `string CorrelationId`, `IReadOnlyList<WorkflowInstanceDiagram> Workflows`
  - `WorkflowInstanceDiagram(string workflowType, string currentState, long version, string dotGraph, string documentJson, string? error)` — properties `WorkflowType`, `CurrentState`, `Version`, `DotGraph`, `DocumentJson`, `Error`

- [ ] **Step 1: Write the failing test** (`[TestClass][TestCategory(TestCategories.Unit)] WorkflowDiagramsEventTest`)

```csharp
[TestMethod]
public void WorkflowDiagramsEvent_RoundTripsThroughJsonUtility()
{
    var ev = new WorkflowDiagramsEvent("c1", [new WorkflowInstanceDiagram("A.Cart", "PricingState", 3, "digraph{}", "{\"states\":{}}", null),
                                              new WorkflowInstanceDiagram("A.Count", "", 0, "", "", "boom")]);
    var copy = JsonUtility.GetObjectForJsonString<WorkflowDiagramsEvent>(JsonUtility.GetJsonStringForObject(ev))!;
    Assert.AreEqual("c1", copy.CorrelationId);
    Assert.AreEqual(2, copy.Workflows.Count);
    var w = copy.Workflows[0];
    Assert.AreEqual(("A.Cart", "PricingState", 3L, "digraph{}", "{\"states\":{}}", (string?)null), (w.WorkflowType, w.CurrentState, w.Version, w.DotGraph, w.DocumentJson, w.Error));
    Assert.AreEqual("boom", copy.Workflows[1].Error);
}

[TestMethod]
public void GetWorkflowDiagramsCommand_RoundTripsCorrelationId() =>
    Assert.AreEqual("c1", JsonUtility.Clone(new GetWorkflowDiagramsCommand("c1")).CorrelationId);
```

- [ ] **Step 2: Run** `dotnet test StatePipes.Test --settings Tests.runsettings --filter "FullyQualifiedName~WorkflowDiagramsEventTest"` — Expected: build FAIL (types missing).
- [ ] **Step 3: Create the three message classes** per Interfaces.
- [ ] **Step 4: Run the same command** — Expected: PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat: workflow interrogation message contract`

---

### Task 2: `WorkflowEngine.Interrogate`

**Files:**
- Modify: `StatePipes/Workflow/Internal/WorkflowMachineFactory.cs`, `StatePipes/Workflow/Internal/WorkflowEngine.cs`
- Test: `StatePipes.Test/Workflow/WorkflowInterrogateTest.cs`

**Interfaces:**
- Consumes: `WorkflowInstanceDiagram` (Task 1).
- Produces:
  - `WorkflowMachineFactory.CreateForDiagram(WorkflowMetadata md, IStatePipesService bus, InMemoryTimerService timers, string currentState) : WorkflowMachine` — calls `Build(..., currentState)`, clears `WorkflowContext.Current` in `finally`; the existing 3-arg overload delegates with `typeof(InitialState).Name`.
  - `WorkflowEngine.Interrogate(string correlationId) : WorkflowInstanceDiagram?` — `internal`. Behavior per spec §4.2: `store.Begin()` + `Load` only, no lock, no commit; null when no row; unknown state → initial-state diagram + `Error = $"Unknown state {record.CurrentState}"` keeping row's state/version/document; any exception → logged, entry `(_type, "", 0, "", "", ex.Message)`. `WorkflowType` is `md.WorkflowTypeName`.

Test class setup: copy `WorkflowEngineTest`'s fields, `Init`, `Cleanup`, `Engine(...)` and `Send(...)` helpers verbatim.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod] public void Interrogate_UnknownId_ReturnsNull() => Assert.IsNull(Engine(typeof(CartWorkflow)).Interrogate("nope"));

[TestMethod]
public void Interrogate_RunningInstance_ReturnsStateVersionDocumentAndHighlightedDiagram()
{
    var e = Engine(typeof(CartWorkflow));
    Send(e, new AddItem { Sku = "a", Qty = 1 }, "c1");
    Send(e, new Checkout(), "c1");
    var rec = _store.Instances.Single();
    var d = e.Interrogate("c1")!;
    Assert.AreEqual((Cart, nameof(PricingState), rec.Version, rec.DocumentJson, (string?)null), (d.WorkflowType, d.CurrentState, d.Version, d.DocumentJson, d.Error));
    Assert.IsTrue(Regex.IsMatch(d.DotGraph, $@"{nameof(PricingState)}[^\n]*style\s*=\s*""?filled"), d.DotGraph);
    Assert.IsFalse(Regex.IsMatch(d.DotGraph, $@"{nameof(CartOpenState)}[^\n]*style\s*=\s*""?filled"), d.DotGraph);
}

[TestMethod]
public void Interrogate_DoesNotMutateAndLaterCommandsStillWork()
{
    var e = Engine(typeof(CartWorkflow));
    Send(e, new AddItem { Sku = "a" }, "c1");
    var before = _store.Instances.Single().Version;
    e.Interrogate("c1");
    Assert.AreEqual(before, _store.Instances.Single().Version);
    Send(e, new AddItem { Sku = "b" }, "c1");
    Assert.AreEqual(before + 1, _store.Instances.Single().Version);
}

[TestMethod]
public void Interrogate_DoesNotRunOnEntryOrSendCommands()
{
    var e = Engine(typeof(CartWorkflow));
    Send(e, new AddItem { Sku = "a" }, "c1");
    Send(e, new Checkout(), "c1");                       // PricingState.OnEntry sends QuotePrice once
    var quotes = _bus.CommandInvocations.OfType<QuotePrice>().Count();
    e.Interrogate("c1");
    Assert.AreEqual(quotes, _bus.CommandInvocations.OfType<QuotePrice>().Count());
}

[TestMethod]
public void Interrogate_WhileLocked_StillReturnsInstance()
{
    var e = Engine(typeof(CartWorkflow));
    Send(e, new AddItem { Sku = "a" }, "c1");
    using var holder = _store.Begin();
    Assert.IsTrue(holder.TryLock(Cart, "c1"));
    Assert.AreEqual(nameof(CartOpenState), e.Interrogate("c1")!.CurrentState);
}

[TestMethod]
public void Interrogate_UnknownPersistedState_ReturnsDiagramAndError()
{
    using (var uow = _store.Begin()) { uow.Upsert(Cart, "c9", "GoneState", "{\"states\":{}}"); uow.Commit(); }
    var d = Engine(typeof(CartWorkflow)).Interrogate("c9")!;
    Assert.AreEqual("GoneState", d.CurrentState);
    StringAssert.Contains(d.Error, "Unknown state GoneState");
    StringAssert.Contains(d.DotGraph, nameof(CartOpenState));
}

[TestMethod]
public void Interrogate_StoreThrows_ReturnsErrorEntry()
{
    var bad = new Mock<IWorkflowStore>();
    bad.Setup(s => s.Begin()).Throws(new InvalidOperationException("db down"));
    var d = Engine(typeof(CartWorkflow), bad.Object).Interrogate("c1")!;
    Assert.AreEqual((Cart, "", 0L, "", "db down"), (d.WorkflowType, d.CurrentState, d.Version, d.DotGraph, d.Error));
}
```

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~WorkflowInterrogateTest"` — Expected: build FAIL (`Interrogate` missing).
- [ ] **Step 3: Add the factory overload and `Interrogate`** per Interfaces. Use the engine's existing `bus` and `timers` constructor parameters.
- [ ] **Step 4: Run the same filter, plus `WorkflowDiagramTest` and `WorkflowEngineTest`** — Expected: all PASS (existing `CreateForDiagram` callers unchanged).
- [ ] **Step 5: `dotnet format`, commit** `feat: WorkflowEngine.Interrogate builds read-only instance diagram`

---

### Task 3: `WorkflowEngineRegistry.Interrogate`

**Files:**
- Modify: `StatePipes/Workflow/Internal/BaseWorkflowTriggerCommandHandler.cs` (class `WorkflowEngineRegistry`)
- Test: append to `StatePipes.Test/Workflow/WorkflowInterrogateTest.cs`

**Interfaces:**
- Consumes: `WorkflowEngine.Interrogate` (Task 2).
- Produces: `WorkflowEngineRegistry.Interrogate(string correlationId) : IReadOnlyList<WorkflowInstanceDiagram>` — non-null results ordered by `WorkflowType` with `StringComparer.Ordinal`.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void Registry_TwoTypesSameId_ReturnsBothOrderedByType()
{
    var reg = new WorkflowEngineRegistry();
    var cart = Engine(typeof(CartWorkflow)); var count = Engine(typeof(CountingWorkflow));
    reg.Add(typeof(CountingWorkflow), count); reg.Add(typeof(CartWorkflow), cart);
    Send(cart, new AddItem { Sku = "a" }, "x"); Send(count, new CountGo(), "x");
    var list = reg.Interrogate("x");
    CollectionAssert.AreEqual(new[] { Cart, typeof(CountingWorkflow).FullName }, list.Select(d => d.WorkflowType).ToArray());
    Assert.AreEqual(0, reg.Interrogate("other").Count);
}

[TestMethod]
public void Registry_OneEngineThrows_OtherStillReturned()
{
    var bad = new Mock<IWorkflowStore>(); bad.Setup(s => s.Begin()).Throws(new InvalidOperationException("db down"));
    var reg = new WorkflowEngineRegistry();
    var cart = Engine(typeof(CartWorkflow));
    reg.Add(typeof(CartWorkflow), cart); reg.Add(typeof(CountingWorkflow), Engine(typeof(CountingWorkflow), bad.Object));
    Send(cart, new AddItem { Sku = "a" }, "x");
    var list = reg.Interrogate("x");
    Assert.AreEqual(2, list.Count);
    Assert.IsNull(list.Single(d => d.WorkflowType == Cart).Error);
    Assert.AreEqual("db down", list.Single(d => d.WorkflowType != Cart).Error);
}
```

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~WorkflowInterrogateTest"` — Expected: build FAIL.
- [ ] **Step 3: Implement** per Interfaces.
- [ ] **Step 4: Run the same filter** — Expected: PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat: WorkflowEngineRegistry.Interrogate across workflow types`

---

### Task 4: `GetWorkflowDiagramsCommandHandler` and registration

**Files:**
- Create: `StatePipes/Workflow/Internal/GetWorkflowDiagramsCommandHandler.cs`
- Modify: `StatePipes/StateMachine/Internal/BaseStateMachineAndFirstStateContainerSetup.cs` — `RegisterOnce`, next to `GetAllStateMachineDiagramsCommandHandler`: `containerBuilder.RegisterType<GetWorkflowDiagramsCommandHandler>().AsSelf().AsImplementedInterfaces();`
- Test: `StatePipes.Test/Workflow/GetWorkflowDiagramsCommandHandlerTest.cs`

**Interfaces:**
- Consumes: Task 1 messages, `WorkflowEngineRegistry.Interrogate` (Task 3).
- Produces: `internal class GetWorkflowDiagramsCommandHandler(IStatePipesService bus, IComponentContext context) : IMessageHandler<GetWorkflowDiagramsCommand>` with `HandleMessage(GetWorkflowDiagramsCommand command, BusConfig? responseInfo, bool isResponse)` per spec §4.4 (`responseInfo == null` → return; `context.ResolveOptional<WorkflowEngineRegistry>()`; missing registry or `string.IsNullOrWhiteSpace(command.CorrelationId)` → empty list; reply with `bus.SendResponse(new WorkflowDiagramsEvent(command.CorrelationId, list), responseInfo)`).

Test helper: `IContainer Ctx(WorkflowEngineRegistry? reg)` builds a `ContainerBuilder`, registering `reg` with `RegisterInstance(reg).AsSelf()` when non-null. Reuse Task 2's `Engine`/`Send` setup (or put these tests in the same file as a second class sharing a small base class — implementer's choice).

- [ ] **Step 1: Write the failing tests**

```csharp
private static readonly BusConfig ReplyTo = new("b", "p", "c", "pw");

[TestMethod]
public void Handle_RepliesWithInstancesAndEchoedId()
{
    var reg = new WorkflowEngineRegistry(); var cart = Engine(typeof(CartWorkflow)); reg.Add(typeof(CartWorkflow), cart);
    Send(cart, new AddItem { Sku = "a" }, "c1");
    using var c = Ctx(reg);
    new GetWorkflowDiagramsCommandHandler(_bus, c).HandleMessage(new GetWorkflowDiagramsCommand("c1"), ReplyTo, false);
    var ev = _bus.EventInvocations.OfType<WorkflowDiagramsEvent>().Single();
    Assert.AreEqual("c1", ev.CorrelationId);
    Assert.AreEqual(nameof(CartOpenState), ev.Workflows.Single().CurrentState);
}

[TestMethod]
public void Handle_NoRegistry_RepliesEmpty()
{
    using var c = Ctx(null);
    new GetWorkflowDiagramsCommandHandler(_bus, c).HandleMessage(new GetWorkflowDiagramsCommand("c1"), ReplyTo, false);
    Assert.AreEqual(0, _bus.EventInvocations.OfType<WorkflowDiagramsEvent>().Single().Workflows.Count);
}

[TestMethod]
public void Handle_BlankId_RepliesEmpty()
{
    var reg = new WorkflowEngineRegistry(); var cart = Engine(typeof(CartWorkflow)); reg.Add(typeof(CartWorkflow), cart);
    Send(cart, new AddItem { Sku = "a" }, "c1");
    using var c = Ctx(reg);
    new GetWorkflowDiagramsCommandHandler(_bus, c).HandleMessage(new GetWorkflowDiagramsCommand("  "), ReplyTo, false);
    Assert.AreEqual(0, _bus.EventInvocations.OfType<WorkflowDiagramsEvent>().Single().Workflows.Count);
}

[TestMethod]
public void Handle_NoResponseInfo_SendsNothing()
{
    using var c = Ctx(new WorkflowEngineRegistry());
    new GetWorkflowDiagramsCommandHandler(_bus, c).HandleMessage(new GetWorkflowDiagramsCommand("c1"), null, false);
    Assert.AreEqual(0, _bus.EventInvocations.OfType<WorkflowDiagramsEvent>().Count());
}

[TestMethod]
public void RegisterOnce_RegistersHandler()
{
    var b = new ContainerBuilder();
    b.RegisterInstance(_bus).As<IStatePipesService>();
    BaseStateMachineAndFirstStateContainerSetup.RegisterOnce(b);
    using var c = b.Build();
    Assert.IsTrue(c.Resolve<IEnumerable<IMessageHandler<GetWorkflowDiagramsCommand>>>().Any());
}
```

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~GetWorkflowDiagramsCommandHandlerTest"` — Expected: build FAIL.
- [ ] **Step 3: Create the handler and add the registration line.**
- [ ] **Step 4: Run the same filter, then the whole `StatePipes.Test.Workflow` namespace** (`--filter "FullyQualifiedName~StatePipes.Test.Workflow"`) — Expected: PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat: GetWorkflowDiagramsCommand handler, always registered`

---

### Task 5: Explorer `WorkflowInterrogation` helper

**Files:**
- Create: `StatePipes.Explorer/NonWebClasses/WorkflowInterrogation.cs`
- Test: `StatePipes.Test/Explorer/WorkflowInterrogationTest.cs`

**Interfaces:**
- Consumes: `WorkflowDiagramsEvent` (Task 1), existing `EventEntry(string fullName, IEvent? obj)`.
- Produces: `internal static class WorkflowInterrogation` (namespace `StatePipes.Explorer.NonWebClasses`):
  - `string? NormalizeInput(string? raw)` — trimmed value, or null when null/whitespace.
  - `bool IsMatchingReply(EventEntry? candidate, EventEntry? snapshot, string pendingCorrelationId)` — true only when `candidate` is non-null, `!ReferenceEquals(candidate, snapshot)`, `candidate.FullName == typeof(WorkflowDiagramsEvent).FullName`, and `JsonUtility.CloneToType<WorkflowDiagramsEvent>(candidate.Obj)?.CorrelationId == pendingCorrelationId` (ordinal).
  - `WorkflowDiagramsEvent? ToEvent(EventEntry entry)` — the `CloneToType` conversion, shared with the component.
  - `string PrettyJson(string json)` — `JToken.Parse(json).ToString(Formatting.Indented)`; on any exception or empty input returns `json`.
  - `string ShortTypeName(string fullName)` — substring after last `.`; whole string if no `.`.
  - `string StatusText(string correlationId, int count)` — `No running workflows for '{id}'` when 0, `1 workflow found`, else `{n} workflows found`.

- [ ] **Step 1: Write the failing tests**

```csharp
private static EventEntry Entry(string id) => new(typeof(WorkflowDiagramsEvent).FullName!, new WorkflowDiagramsEvent(id, []));

[TestMethod] public void NormalizeInput_Trims() => Assert.AreEqual("abc", WorkflowInterrogation.NormalizeInput("  abc \t"));
[TestMethod] public void NormalizeInput_WhitespaceIsNull() => Assert.IsNull(WorkflowInterrogation.NormalizeInput("   "));
[TestMethod] public void IsMatchingReply_NewEntrySameId_True() => Assert.IsTrue(WorkflowInterrogation.IsMatchingReply(Entry("c1"), null, "c1"));
[TestMethod] public void IsMatchingReply_SameInstanceAsSnapshot_False() { var e = Entry("c1"); Assert.IsFalse(WorkflowInterrogation.IsMatchingReply(e, e, "c1")); }
[TestMethod] public void IsMatchingReply_OtherId_False() => Assert.IsFalse(WorkflowInterrogation.IsMatchingReply(Entry("old"), null, "c1"));
[TestMethod] public void IsMatchingReply_Null_False() => Assert.IsFalse(WorkflowInterrogation.IsMatchingReply(null, null, "c1"));
[TestMethod] public void PrettyJson_Indents() => StringAssert.Contains(WorkflowInterrogation.PrettyJson("{\"a\":1}"), "\n");
[TestMethod] public void PrettyJson_InvalidReturnsInput() => Assert.AreEqual("{not json", WorkflowInterrogation.PrettyJson("{not json"));
[TestMethod] public void ShortTypeName_AfterLastDot() => Assert.AreEqual("CartWorkflow", WorkflowInterrogation.ShortTypeName("A.B.CartWorkflow"));
[TestMethod] public void StatusText_Variants()
{
    Assert.AreEqual("No running workflows for 'c1'", WorkflowInterrogation.StatusText("c1", 0));
    Assert.AreEqual("1 workflow found", WorkflowInterrogation.StatusText("c1", 1));
    Assert.AreEqual("2 workflows found", WorkflowInterrogation.StatusText("c1", 2));
}
```

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~WorkflowInterrogationTest"` — Expected: build FAIL.
- [ ] **Step 3: Implement** per Interfaces.
- [ ] **Step 4: Run the same filter** — Expected: PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat: Explorer workflow interrogation helper`

---

### Task 6: Explorer WORKFLOW tab

**Files:**
- Create: `StatePipes.Explorer/Components/Pages/WorkflowDiagramViewer.razor`, `.razor.cs`, `.razor.css`
- Modify: `StatePipes.Explorer/Components/Pages/TabEnum.cs` → `public enum TabEnum { ObjectEditor, LogFile, StateMachine, Workflow }`
- Modify: `StatePipes.Explorer/Components/Pages/Home.razor` — after the STATE MACHINE `<Tab>`:
  `<Tab Title="WORKFLOW" OnClick="@(() => ToggleDisplayMode(TabEnum.Workflow))"><Content><WorkflowDiagramViewer InstanceGuid=@InstanceGuid TabId=@_showTab></WorkflowDiagramViewer></Content></Tab>`

**Interfaces:**
- Consumes: Task 1 messages, Task 5 helper, `IStatePipesHandler.SendCommand(Guid, string, string)`, `GetEventJsons(Guid)`, `GetIsConnectedToBroker(Guid)`, `diagrams.js` `renderDot(dot, divId)`.
- Produces: component `WorkflowDiagramViewer` with `[Parameter] Guid InstanceGuid`, `[Parameter] TabEnum TabId`; `@implements IAsyncDisposable`; injects `IStatePipesHandler` and `IJSRuntime`.

Component behavior is spec §5.2–5.3; decisions not spelled out there:
- Import `/diagrams.js` in `OnAfterRenderAsync(firstRender)` as `StateMachineDiagramViewer` does.
- Fields: `_input`, `_pendingCorrelationId`, `_snapshot` (EventEntry?), `_sentAt` (DateTime UTC), `_result` (WorkflowDiagramsEvent?), `_status`, `_needsDraw`, `System.Threading.Timer? _pollTimer`, `TabEnum _prevTabId`.
- `Interrogate()`: `NormalizeInput`; return if null, pending, or not connected. Snapshot = latest `WorkflowDiagramsEvent` entry from `GetEventJsons`; set status `Waiting for reply…`; send `new GetWorkflowDiagramsCommand(id)` via `JsonUtility.GetJsonStringForObject`; start the 250 ms timer.
- Timer tick: on `IsMatchingReply` → dispose timer, `_result = ToEvent(...)`, status via `StatusText`, `_needsDraw = true`, clear pending, `await InvokeAsync(StateHasChanged)`. On `UtcNow - _sentAt >= 10 s` → status `No reply after 10 s`, clear pending, dispose timer, `StateHasChanged`. Wrap in try/catch → `Log?.LogException`.
- Draw in `OnAfterRenderAsync` when `_needsDraw`: for each workflow with non-empty `DotGraph`, `renderDot(dot, $"WorkflowDiagram{i}")`; clear `_needsDraw`. In `OnParametersSet`, when `TabId` changes to `TabEnum.Workflow` and `_result != null`, set `_needsDraw = true` (redraw on tab re-show).
- Markup per spec §5.2: `<input @bind="_input" @bind:event="oninput" @onkeydown=...>` (Enter → `Interrogate`), button `INTERROGATE` disabled when `NormalizeInput(_input) is null || _pendingCorrelationId != null || !connected`; per workflow a header `@ShortTypeName(WorkflowType) · state @CurrentState · v@Version` with `title=@WorkflowType`, `<div id="WorkflowDiagram@i" style="border:1px solid;width:100%;height:500px">` only when `DotGraph` non-empty, `<CollapsableDivision TitleName="Document" IsExpandedInitialState="false">` containing `<pre>@PrettyJson(DocumentJson)</pre>` when `DocumentJson` non-empty, and `<div class="wf-error">@Error</div>` when `Error` non-null. Connection state is refreshed on each render and timer tick via `GetIsConnectedToBroker`.
- `.razor.css`: `.wf-error { color: red; }`, header styling minimal.
- `DisposeAsync`: dispose timer and JS module, as `StateMachineDiagramViewer` does.

- [ ] **Step 1: Add enum value, component files, and the `Home.razor` tab.**
- [ ] **Step 2: Build** `dotnet build` — Expected: 0 errors, no new warnings in Explorer.
- [ ] **Step 3: Manual verification.** Run a sample service that declares a workflow (in-memory store) plus Explorer (`dotnet run --project StatePipes.Explorer`, or the `run` skill). Drive one instance to a non-initial state with the HOME tab's command editor. In WORKFLOW tab verify: (a) correct id → diagram with that state filled yellow, header, collapsible document; (b) unknown id → `No running workflows for '<id>'`; (c) interrogate the same id twice after another transition → second render shows the new state; (d) `"  id  "` works (trimmed); (e) switching to HOME and back redraws the diagram; (f) stop the service → `No reply after 10 s`. Record results in the task report.
- [ ] **Step 4: Full verification** — `dotnet format`, `dotnet build`, `dotnet test --settings Tests.runsettings` — Expected: all PASS.
- [ ] **Step 5: Commit** `feat: Explorer WORKFLOW tab interrogates workflows by correlationId`
