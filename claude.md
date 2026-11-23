# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

StatePipes is a .NET (net11.0) state-oriented programming framework for SCADA, robotics and IoT microservices, built on
[Stateless](https://github.com/dotnet-state-machine/stateless) and Autofac. The NuGet package is `StatePipes/`; the other
projects are tooling around it. `docs/DeveloperGuide.md` is the authoritative description of the programming model, and
`docs/StateMachineComposition.md` covers hierarchical state composition in depth.

## Commands

The solution file is `StatePipes.slnx`. Several projects (the test project, ServiceCreatorTool, the installer) target
`net11.0-windows`, so the full solution builds only on Windows.

```
dotnet build StatePipes.slnx
dotnet test StatePipes.Test/StatePipes.Test.csproj --settings Tests.runsettings --filter "TestCategory=Unit"   # what CI runs
dotnet test --settings Tests.runsettings --filter "FullyQualifiedName~StatePipes.Test.Workflow.WorkflowTypesTest"  # one class
dotnet test --settings Tests.runsettings --filter "FullyQualifiedName~ClaudePlugin"   # plugin/template consistency tests
dotnet format
```

- Tests are MSTest, run serially (`[assembly: DoNotParallelize]`, 1 worker). Categories are in
  `StatePipes.Test/TestCategories.cs`: `Unit`, `Integration`, `TimingCritical`. `Integration` tests need live
  infrastructure (PostgreSQL, a broker, the OPC UA server) and are not run in CI.
- Releases are automated by `.github/workflows/release.yml` on every merge to `main`: it bumps `VersionPrefix` in
  `SolutionInfo.proj` and the plugin version, builds, pushes Docker images and the NuGet package. Don't bump versions by
  hand. The bump size comes from PR labels `release:major|minor|patch` (default patch).

## Architecture

### Runtime model (`StatePipes/`)
- A **service** is one bounded context, hosted by `StatePipesService` (`Comms/Internal`), which is a
  `TaskWrapper<ReceivedCommandMessage>`: received commands are queued and processed one at a time on a single worker, so
  state code runs single-threaded and must never block.
- `ServiceConfiguration` (`Comms/`) is everything a service needs to start: `BusConfig` (broker URI, exchange name,
  client cert), the `IContainerSetup` type, `ProxyConfigurations`, `ProxySubstitutions`, `ServiceArgs`, and an optional
  `WorkflowStoreConfig` (PostgreSQL).
- **Transport** is abstracted behind `ITransport`: `RabbitMqTransport` (default) or `KafkaTransport`, selected by the
  broker URI (`ssl://` → Kafka) in `TransportFactory`. Commands arrive on `<exchange>.commands`, public events leave on
  `<exchange>.events`.
- **State machines** are marker interfaces (`: IStateMachine`). Each state is a class deriving from
  `BaseStateMachineState<TMachine>` (or `ParentedBaseStateMachineState<TMachine, TParent>` for substates). States
  declare transitions with guard methods attributed `[PermitIf(typeof(Dest))]`, `[PermitReentryIf]` or `[IgnoreIf]`;
  `StateHandlerScanner` discovers them via reflection and `BaseStateMachine` builds the Stateless configuration. Never
  configure Stateless directly.
- Hierarchy: a parent's first child implements `IFirstSubstate` and is entered via the internal `MoveToState` trigger
  (`MoveToStateWorker`). `BaseStateMachine.OnTransitioned` has special handling for reentry on a parent while in a child
  (Stateless's LCA algorithm skips the parent's `OnEntry`); keep that in mind when touching transition code.
- Messaging from inside a state: `SendCommand` (trigger to a machine/service), `PublishEvent` (broadcast),
  `SendResponse` (reply to the sender's `BusConfig`), `FireExternal<OtherMachine, Trigger>` (cross-machine within a
  service). **Visibility is routing**: `public` triggers/events are part of the service's broker API; `internal` ones never
  leave the process.
- **Workflows** (`Workflow/`) are state machines with many persisted instances keyed by `CorrelationId`, stored in
  PostgreSQL. Workflow states have parameterless constructors only (no DI) and all their fields are serialized.
- **Proxies** to other services are generated code (`BaseGeneratedProxy`): the other service's public triggers become
  local internal `...To<Moniker>Event`s and its public events become local internal `...From<Moniker>Trigger`s.
- Test harnesses for consumers live in the package itself: `StateMachine/Test/StateMachineForTest` and
  `WorkflowForTest` (in-memory store and `TimeProvider` clock).

### Tooling projects
- `StatePipes.ServiceCreatorTool`: the Visual Studio External Tools that scaffold services, states, triggers, guards,
  timers, proxies and workflows from `.sample` templates in `Resources/`. Generated code contains `// Injection Point`
  markers that the tools insert into; don't remove them.
- `StatePipes.ClaudePlugin`: a Claude Code plugin whose skills (`statepipes-scaffold`, `-conventions`, `-verify`, …)
  replicate the ServiceCreatorTool output. Tests in `StatePipes.Test/ClaudePlugin/` check the skill recipes against the
  real templates and generators, so a change to either side must keep them in sync.
- `StatePipes.Explorer` (web UI), `StatePipes.BrokerProxy`, `StatePipes.OpcUaBridge`: Docker-hosted tools published as
  `bigfish88/*` images. `OpcUaServer` is a test OPC UA server. `StatePipes.Diagrammer` renders state machine diagrams.

## Rules
- Never use blocking calls (`.Result`, `.Wait()`) in states, guards or message-processing loops; guards must be fast.
- Put side effects of entering/leaving a state in `OnEntry`/`OnExit` (timers, "entered" events, resources).
- Trigger and event payloads must be immutable and carry value objects or plain values, never entities.
- Check state machine changes against the design criteria in `docs/Paper.md`.
- Run `dotnet format` after changing code, before calling the task done.
