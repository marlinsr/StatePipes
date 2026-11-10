# <Solution> Project Memory

StatePipes microservices built on the state-scoped command handler pattern (StatePipes <version>). Each service is a
class library of state machines, a `.Service` host, and a `.Test` project. Below, `<Prefix>` means a service's
class library, as listed in the Services table.

## Services

| Service | Class library | Model |
|---|---|---|
<!-- One row per service. New StatePipes Service adds rows above this line. -->

## Dev & Test Commands

- **Build:** `dotnet build <Solution>.slnx`
- **Test:** `dotnet test <Solution>.slnx --settings Tests.runsettings`
- **Format:** `dotnet format <Solution>.slnx`
- **Run locally:** `RunScript/DockerInfrastructureStart.ps1`, then `RunScript/Start.ps1` (`Stop.ps1` and
  `DockerInfrastructureStop.ps1` to stop). See `SetupAndRunInstructions.pdf`.

## How to work in this repository

- Use the `statepipes` Claude Code plugin skills: `statepipes-domain-fit` and `statepipes-domain-modeling` before new
  behavior, `statepipes-scaffold` for every structural change, `statepipes-conventions` for code, and
  `statepipes-verify` before calling a change done.
- Change the approved model (`<Prefix>/StatePipesModel.md`) first, get it approved, then change the code to match.
- Structural changes (state machine, state, trigger, guard, periodic timer, event, proxy) go through the scaffold
  operations, so files, names and injection points match what the StatePipes Visual Studio External Tools produce.
- Generate proxies with the ServiceCreatorTool run headless. Never write or hand-edit files under `Proxies/`.

## Domain-Driven Design

- Entities go in `<Prefix>/Entities/` (namespace `<Prefix>.Entities`). Create the folder when adding the first one.
- Value objects go in `<Prefix>/ValueObjects/` (namespace `<Prefix>.ValueObjects`) as immutable records.
- Trigger and event payloads carry value objects or plain values, never entities.
- No domain types in `StateMachines/`, `Builders/`, `Events/`, or invented folders such as `Models/`.

## Workflows

A workflow (`: IWorkflow`) is a state machine with many persisted instances, one per CorrelationId (e.g. a cart),
stored in PostgreSQL (`WorkflowStore` in `Builders/DefaultServiceConfiguration.cs`).

- Workflow states derive from `BaseWorkflowState<T>` / `ParentedBaseWorkflowState<T, TParent>`.
- Workflow states have a parameterless constructor only: no dependency injection.
- Every field of every workflow state is persisted with the instance, so fields must be JSON-serializable.
- Reach singleton services only by `SendCommand` to a singleton state machine, passing the CorrelationId; the singleton
  replies with a `BaseWorkflowTriggerCommand<T>` carrying that CorrelationId. Never `FireExternal`.
- Every workflow has at least one state implementing `IFinalWorkflowState`; entering it deletes the instance and its timers.
- Every workflow trigger derives from `BaseWorkflowTriggerCommand<T>` and carries the instance's `CorrelationId`.
- Set `CorrelationId = CorrelationId` on every message a workflow state sends to itself, timer messages included; the
  framework never stamps it.
- Use `[IgnoreIf]` for timer messages that can arrive after the state that started the timer has been left.

## Behavioral Rules

- Never block on async work: no `.Result` or `.Wait()` in state machine code or async loops. Use `async`/`await`
  and wrap long-running or fire-and-forget work with `TaskWrapper`.
- Side effects of entering or leaving a state go in `OnEntry` / `OnExit`.
- Never delete or edit `// Injection Point N, Do Not Delete or Modify` lines or `//@#$...@#$` using prefixes.
- Proxy topology is configured only in `Builders/DefaultServiceConfiguration.cs`.
- Run `dotnet format` before completing any change.
