# StatePipes State Machine Composition Guide

A comprehensive reference for building, connecting, and testing state machines using the StatePipes framework.

---

## Table of Contents

1. [Core Concepts Overview](#1-core-concepts-overview)
2. [The Type Hierarchy](#2-the-type-hierarchy)
3. [Defining a State Machine](#3-defining-a-state-machine)
4. [Defining Triggers](#4-defining-triggers)
5. [Defining States](#5-defining-states)
6. [Composing Transitions with StateConfigurationWrapper](#6-composing-transitions-with-stateconfigurationwrapper)
7. [Guard Attributes](#7-guard-attributes)
8. [Hierarchical States (Substates)](#8-hierarchical-states-substates)
9. [Container Setup and Registration](#9-container-setup-and-registration)
10. [Cross-Machine Firing](#10-cross-machine-firing)
11. [Publishing Events and Sending Commands](#11-publishing-events-and-sending-commands)
12. [Testing with StateMachineForTest](#12-testing-with-statemachinefortest)
13. [Generics Reference Card](#13-generics-reference-card)
14. [Attributes Reference Card](#14-attributes-reference-card)
15. [End-to-End Example: Traffic Light](#15-end-to-end-example-traffic-light)

---

## 1. Core Concepts Overview

StatePipes wraps the [Stateless](https://github.com/dotnet-state-machine/stateless) library with a strongly-typed, attribute-driven, dependency-injected API. The key ideas:

| Concept | Role |
|---------|------|
| **State machine** | A marker type (`IStateMachine`) that acts as the generic key binding all related states and triggers together. |
| **State** | A class derived from `BaseStateMachineState<TStateMachine>` that declares transitions by annotating methods with `[PermitIf]`, `[IgnoreIf]`, or `[PermitReentryIf]`. |
| **Trigger** | A message class derived from `BaseTriggerCommand<TStateMachine>` that is sent over the bus to advance the machine. |
| **Guard** | A method on a state class annotated with `[PermitIf]`, `[IgnoreIf]`, or `[PermitReentryIf]` that dynamically allows or blocks a transition. |
| **Event** | A broadcast message (`IEvent`) that a state publishes to the outside world via `PublishEvent<TEvent>()`. |
| **Bus** | `IStatePipesService` — the message bus that routes triggers, commands, and events. |

The framework uses **Autofac** for dependency injection. Container setup classes (`IContainerSetup`) register all participants and wire the machine during `Build()`.

---

## 2. The Type Hierarchy

```
IMessage
├── ICommand          (point-to-point request)
│   └── ITrigger      (state-machine transition command)
│       └── BaseTriggerCommand<TStateMachine>   ← your triggers inherit here
└── IEvent            (broadcast notification)

IStateMachine                                   ← marker; your machine interface
IStateMachineState                              ← contract: Configure(StateConfigurationWrapper)
    └── BaseStateMachineState<TStateMachine>    ← base class for every state
        └── ParentedBaseStateMachineState<TStateMachine, TParentState>
                                                ← base class for sub-states
```

### Interface Definitions

```csharp
// StatePipes.Interfaces
public interface IMessage { }
public interface ICommand : IMessage { }
public interface IEvent   : IMessage { }
public interface ITrigger : ICommand, IMessage { }

public interface IStateMachine { }

public interface IStateMachineState
{
    void Configure(StateConfigurationWrapper stateConfig);
}

public interface IFirstStateForStateMachine { }  // marker: designates entry state
public interface IFirstSubstate { }              // marker: designates entry sub-state
public interface IInitTrigger { }                // marker: framework-internal init
```

---

## 3. Defining a State Machine

A state machine is declared as an empty class (or interface) implementing `IStateMachine`. It functions purely as a **generic type parameter** — a compile-time key that binds states and triggers together.

```csharp
// MyService/StateMachines/PumpStateMachine.cs
namespace MyService.StateMachines
{
    public class PumpStateMachine : IStateMachine
    {
        // No members needed. Acts as a type-safe key.
    }
}
```

> **Why an empty class?**
> All states inherit `BaseStateMachineState<PumpStateMachine>` and all triggers inherit `BaseTriggerCommand<PumpStateMachine>`. The shared generic argument creates a compile-time relationship that the framework uses to automatically discover, register, and wire all participants. Adding members to the machine class would not give them access to state lifecycle — that belongs in state classes.

---

## 4. Defining Triggers

A trigger is a message sent over the bus that causes a state transition. Define one class per distinct transition signal, inheriting `BaseTriggerCommand<TStateMachine>`.

```csharp
// MyService/Triggers/StartPumpTrigger.cs
using StatePipes.StateMachine;

namespace MyService.Triggers
{
    public class StartPumpTrigger : BaseTriggerCommand<PumpStateMachine>
    {
        // Add payload properties as needed.
        public int TargetRpm { get; init; }
    }
}
```

```csharp
// MyService/Triggers/StopPumpTrigger.cs
public class StopPumpTrigger : BaseTriggerCommand<PumpStateMachine> { }

// MyService/Triggers/FaultDetectedTrigger.cs
public class FaultDetectedTrigger : BaseTriggerCommand<PumpStateMachine>
{
    public string FaultCode { get; init; } = string.Empty;
}
```

> **Triggers must be immutable.** A trigger instance is dispatched on the bus and may be read by multiple handlers. Mutating a trigger after dispatch produces undefined behaviour. Use `init`-only properties as shown above, or prefer C# `record` types for brevity:
>
> ```csharp
> public record StartPumpTrigger(int TargetRpm)      : BaseTriggerCommand<PumpStateMachine>;
> public record StopPumpTrigger                      : BaseTriggerCommand<PumpStateMachine>;
> public record FaultDetectedTrigger(string FaultCode) : BaseTriggerCommand<PumpStateMachine>;
> ```
>
> `record` types generate value equality, a deconstructor, and `init`-only positional properties automatically, making them the most concise way to express immutable trigger messages.

### Generic constraint on `BaseTriggerCommand<TStateMachine>`

```csharp
// Framework definition (simplified)
public class BaseTriggerCommand<StateMachineType> : ITrigger
    where StateMachineType : IStateMachine
{
}
```

The `where StateMachineType : IStateMachine` constraint means:
- Only a valid state machine marker can fill `TStateMachine`.
- The framework can call `GetLoadableTypes()` on the assembly, filter to `BaseTriggerCommand<PumpStateMachine>`, and automatically register a `BaseTriggerCommandHandler<TTrigger, TStateMachine>` for each one — no manual wiring needed.

---

## 5. Defining States

Each state is a class that:
1. Inherits `BaseStateMachineState<TStateMachine>`.
2. Declares transitions by annotating methods with `[PermitIf]`, `[IgnoreIf]`, or `[PermitReentryIf]`. The base `Configure` implementation scans for these attributes automatically — no `Configure` override is needed for transition declarations.
3. Optionally overrides `Configure` only for wiring that has no attribute equivalent (e.g., `OnEntryFrom`). Always call `base.Configure(stateConfig)` first when doing so.
4. Optionally overrides lifecycle hooks: `OnEntry`, `OnExit`, `OnActivate`, `OnDeActivate`.

### 5.1 Minimal State

```csharp
using StatePipes.StateMachine;
using StatePipes.Comms;
using MyService.Triggers;

namespace MyService.States
{
    public class IdleState
        : BaseStateMachineState<PumpStateMachine>, IFirstStateForStateMachine
    {
        [PermitIf(typeof(RunningState))]
        private bool OnStartPump(StartPumpTrigger trigger, BusConfig? responseInfo) => true;

        [IgnoreIf]
        private bool OnStopPump(StopPumpTrigger trigger, BusConfig? responseInfo) => true;

        public override void OnEntry()
        {
            // Called every time we enter Idle (e.g. log, reset counters)
        }
    }
}
```

> **`IFirstStateForStateMachine`** is a marker interface. The framework scans the assembly for a type that is both a `BaseStateMachineState<TStateMachine>` and `IFirstStateForStateMachine` to determine which state is entered after initialization.

### 5.2 State with Payload Access

Inside a state method you can retrieve the current trigger — including its payload — using the protected generic helper `GetCurrentTrigger<TTrigger>()`.

```csharp
public class RunningState : BaseStateMachineState<PumpStateMachine>
{
    [PermitIf(typeof(IdleState))]
    private bool OnStop(StopPumpTrigger trigger, BusConfig? responseInfo) => true;

    [PermitIf(typeof(FaultState))]
    private bool OnFault(FaultDetectedTrigger trigger, BusConfig? responseInfo) => true;

    public override void OnEntry()
    {
        // Read the trigger that brought us here
        var trigger = GetCurrentTrigger<StartPumpTrigger>();
        if (trigger != null)
        {
            // Use trigger.TargetRpm to spin up hardware
        }
    }
}
```

### 5.3 Lifecycle Hooks

| Method | When Invoked |
|--------|-------------|
| `OnEntry()` | Every time the state is entered (after any transition into this state). |
| `OnExit()` | Every time the state is left. |
| `OnActivate()` | When the underlying Stateless machine activates (once at startup for the initial state). |
| `OnDeActivate()` | When the underlying Stateless machine deactivates. |

---

## 6. Composing Transitions with `StateConfigurationWrapper`

`StateConfigurationWrapper` is a **fluent builder** returned by every transition method. You chain calls to declare all rules for a state. Every method returns a new `StateConfigurationWrapper`, allowing safe immutable chaining.

### 6.1 API Summary

```csharp
// Unconditional transitions
stateConfig.Permit<TTrigger, TDestinationState>()
stateConfig.PermitReentry<TTrigger>()

// Conditional transitions (guard is a lambda)
stateConfig.PermitIf<TTrigger, TDestinationState>(Func<bool> guard, string? description)
stateConfig.PermitReentryIf<TTrigger>(Func<bool> guard, string? description)

// Suppress / ignore a trigger in this state
stateConfig.Ignore<TTrigger>()
stateConfig.IgnoreIf<TTrigger>(Func<bool> guard, string? description)

// Parent-child nesting
stateConfig.SubstateOf<TSuperState>()

// Internal move-to chain (used by ParentedBaseStateMachineState automatically)
stateConfig.MoveToState<TDestinationState>()

// Register an event type for diagram annotation
stateConfig.RegisterEvent<TEvent>()

// Lifecycle hooks (chained within Configure)
stateConfig.OnEntry(action)
stateConfig.OnExit(action)
stateConfig.OnActivate(action)
stateConfig.OnDeactivate(action)
stateConfig.OnEntryFrom<TTrigger>(action)
```

### 6.2 Generic Constraints on Transition Methods

```csharp
// Both TTrigger and TDestinationState are constrained:
public StateConfigurationWrapper Permit<TTrigger, TDestinationState>()
    where TTrigger         : ITrigger
    where TDestinationState: IStateMachineState
```

This means the compiler rejects any attempt to use a non-trigger or non-state type as an argument — you get a compile error rather than a runtime exception.

### 6.3 Attribute-Based Declarations with Optional Configure Override

Transitions are declared with attributes. `Configure` is only overridden when wiring that has no attribute equivalent is needed — such as `OnEntryFrom`.

```csharp
// Transitions declared via attributes — no Configure override needed for these
[PermitIf(typeof(IdleState))]
private bool OnStop(StopPumpTrigger trigger, BusConfig? responseInfo) => true;

[PermitIf(typeof(FaultState))]
private bool OnFault(FaultDetectedTrigger trigger, BusConfig? responseInfo) => true;

[PermitReentryIf("Allow re-sending start while running")]
private bool OnRestart(StartPumpTrigger trigger, BusConfig? responseInfo) => true;

// Override Configure only for OnEntryFrom, which has no attribute equivalent
public override void Configure(StateConfigurationWrapper stateConfig)
{
    base.Configure(stateConfig);

    stateConfig.OnEntryFrom<StartPumpTrigger>(() =>
    {
        var t = GetCurrentTrigger<StartPumpTrigger>();
        // handle re-entry with updated RPM
    });
}
```

---

## 7. Guard Attributes

Instead of writing lambda guards inline in `Configure`, you can annotate **methods** on the state class. The base class `Configure` auto-scans for these attributes via reflection and wires them. This approach keeps transition logic close to its implementation and produces richer diagrams.

### 7.1 `[PermitIf]`

Permits a transition **only when the method returns `true`**.

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class PermitIf(Type destinationState, string? guardDescription = null) : Attribute
{
    public Type DestinationState { get; } = destinationState;
    public string? GuardDescription { get; } = guardDescription;
}
```

**Usage:**

```csharp
public class RunningState : BaseStateMachineState<PumpStateMachine>
{
    // Method signature: (TTrigger trigger, BusConfig? responseInfo) → bool
    [PermitIf(typeof(IdleState), "Only stop if RPM is zero")]
    private bool CanStop(StopPumpTrigger trigger, BusConfig? responseInfo)
    {
        // Could inspect trigger properties or external service state
        return GetCurrentRpm() == 0;
    }
}
```

> **Signature contract**: The method must take `(TTriggerType trigger, BusConfig? responseInfo)` and return `bool`. The framework reads `method.GetParameters()[0].ParameterType` to know which trigger type to look for.

### 7.2 `[IgnoreIf]`

Suppresses (silently drops) the trigger **when the method returns `true`**.

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class IgnoreIf(string? guardDescription = null) : Attribute
{
    public string? GuardDescription { get; } = guardDescription;
}
```

**Usage:**

```csharp
[IgnoreIf("Ignore stop if already stopping")]
private bool ShouldIgnoreStop(StopPumpTrigger trigger, BusConfig? responseInfo)
{
    return _alreadyStopping;
}
```

> Unlike `[PermitIf]`, `[IgnoreIf]` does not need a destination state — the trigger is consumed without a transition.

### 7.3 `[PermitReentryIf]`

Re-enters the **same state** (firing `OnExit` → `OnEntry`) **when the method returns `true`**.

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class PermitReentryIf(string? guardDescription = null) : Attribute
{
    public string? GuardDescription { get; } = guardDescription;
}
```

**Pattern 1 — Conditional re-entry:** return `true` to cycle the state when the trigger warrants it.

```csharp
[PermitReentryIf("Refresh running config if speed changed")]
private bool ShouldRestart(StartPumpTrigger trigger, BusConfig? responseInfo)
{
    return trigger.TargetRpm != _lastTargetRpm;
}
```

**Pattern 2 — Process a trigger without changing state:** return `false` to run the method body as a side effect while leaving the state machine untouched. Because the guard returns `false`, Stateless does not execute the re-entry transition — `OnExit` and `OnEntry` are never called — but all code before the `return` did execute.

```csharp
[PermitReentryIf("Handle status poll in-place")]
private bool OnStatusPoll(StatusPollTrigger trigger, BusConfig? responseInfo)
{
    // Perform processing — read sensors, update internal fields, publish an event, etc.
    _lastPollTime = DateTimeOffset.UtcNow;
    PublishEvent(new StatusSnapshot { Rpm = _currentRpm, PollTime = _lastPollTime });

    return false;  // Do NOT re-enter: OnExit/OnEntry remain silent
}
```

> Use this pattern whenever a trigger must be acknowledged and acted upon without disturbing the current state's lifecycle. It is the correct alternative to `[IgnoreIf]` when the trigger still needs to produce an observable side effect.

### 7.4 How the Framework Wires Guards

The auto-scanning logic inside `BaseStateMachineState.Configure`:

```csharp
// Pseudocode of the reflection scan in BaseStateMachineState.Configure
foreach (var method in stateType.GetMethods(Public | NonPublic | Instance))
{
    if (method.GetCustomAttribute<PermitIf>() is { } permitIfAttr)
    {
        var triggerType = method.GetParameters()[0].ParameterType;
        bool guard() => (bool)method.Invoke(this,
            [GetCurrentTrigger(triggerType), GetCurrentResponseInfo()])!;
        stateConfig.PermitIf(triggerType, permitIfAttr.DestinationState, guard, permitIfAttr.GuardDescription);
    }
    // similar for IgnoreIf and PermitReentryIf ...
}
```

Because the guard closure captures the method and the live state instance, the guard runs **at transition time**, not at configuration time.

---

## 8. Hierarchical States (Substates)

StatePipes supports Stateless's hierarchical (superstate/substate) model through `ParentedBaseStateMachineState<TStateMachine, TParentState>`.

### 8.1 Defining a Superstate

A superstate is any ordinary `BaseStateMachineState<TStateMachine>`. It declares transitions that apply to **all its children** — a trigger permitted in a superstate is also permitted when the machine is in any substate, unless the substate overrides it.

```csharp
public class OperationalState : BaseStateMachineState<PumpStateMachine>
{
    // Emergency stop is valid from ANY substate of Operational
    [PermitIf(typeof(FaultState))]
    private bool OnFault(FaultDetectedTrigger trigger, BusConfig? responseInfo) => true;
}
```

### 8.2 Defining a Substate

Inherit `ParentedBaseStateMachineState<TStateMachine, TParentState>`. The framework calls `stateConfig.SubstateOf<TParentState>()` automatically in the base `Configure`.

```csharp
public class IdleState
    : ParentedBaseStateMachineState<PumpStateMachine, OperationalState>,
      IFirstStateForStateMachine
{
    // SubstateOf<OperationalState> is registered automatically by the base class
    [PermitIf(typeof(RunningState))]
    private bool OnStart(StartPumpTrigger trigger, BusConfig? responseInfo) => true;
}

public class RunningState
    : ParentedBaseStateMachineState<PumpStateMachine, OperationalState>
{
    [PermitIf(typeof(IdleState))]
    private bool OnStop(StopPumpTrigger trigger, BusConfig? responseInfo) => true;
}
```

### 8.3 `IFirstSubstate` — Automatic Entry into a Substate

When the machine enters a superstate it needs to know which child to enter first. Implement `IFirstSubstate` on that child state. `BaseStateMachineState.Configure` scans the assembly for a type that:

- Implements `IFirstSubstate`, **and**
- Has `ParentedBaseStateMachineState<TStateMachine, **ThisType**>` somewhere in its inheritance chain.

It then calls `stateConfig.MoveToState(foundType)` automatically, so no explicit wiring is required.

```csharp
public class IdleState
    : ParentedBaseStateMachineState<PumpStateMachine, OperationalState>,
      IFirstStateForStateMachine,
      IFirstSubstate       // ← tells the framework to enter here when OperationalState is entered
{ ... }
```

### 8.4 Generic Constraints on `ParentedBaseStateMachineState`

```csharp
public class ParentedBaseStateMachineState<StateMachineType, ParentStateType>
    : BaseStateMachineState<StateMachineType>
    where StateMachineType : IStateMachine
    where ParentStateType  : BaseStateMachineState<StateMachineType>
```

`ParentStateType` is constrained to `BaseStateMachineState<StateMachineType>`, ensuring:
- The parent is a state in the **same** machine (same `TStateMachine`).
- You cannot accidentally parent a state to a state in a different machine.

---

## 9. Container Setup and Registration

The framework provides several `IContainerSetup` implementations depending on the scenario.

### 9.1 `IContainerSetup` Contract

```csharp
public interface IContainerSetup
{
    void Register(ContainerBuilder containerBuilder); // DI registrations
    void Build(IContainer container);                 // post-build wiring
}
```

### 9.2 Production: Auto-discover First State

Use when the first state is marked with `IFirstStateForStateMachine`. The framework scans the assembly.

```csharp
// In your application's IContainerSetup implementation:
using StatePipes.StateMachine.Internal;

public class MyServiceContainerSetup : IContainerSetup
{
    public void Register(ContainerBuilder containerBuilder)
    {
        // Registers StateMachineManager, BaseStateMachine, command handlers,
        // all states, and the synthetic init trigger.
        var smSetup = new BaseStateMachineContainerSetup(typeof(PumpStateMachine));
        smSetup.Register(containerBuilder);
    }

    public void Build(IContainer container)
    {
        var smSetup = new BaseStateMachineContainerSetup(typeof(PumpStateMachine));
        smSetup.Build(container);  // configures and activates the machine
    }
}
```

**What happens inside `BaseStateMachineContainerSetup`:**

1. Finds the type that is both `BaseStateMachineState<TStateMachine>` and `IFirstStateForStateMachine`.
2. Delegates to `BaseStateMachineAndFirstStateContainerSetup`.

### 9.3 Production: Explicit First State

Use `StateMachineAndFirstStateContainerSetup<TStateMachine, TFirstState>` when you want to specify the first state at the call site.

```csharp
// Generic version with explicit first state
var setup = new StateMachineAndFirstStateContainerSetup<PumpStateMachine, IdleState>();
// where PumpStateMachine : IStateMachine
// where IdleState        : IStateMachineState
```

**Generic constraints:**

```csharp
internal class StateMachineAndFirstStateContainerSetup<StateMachineType, NextStateAfterInit>
    where StateMachineType    : IStateMachine
    where NextStateAfterInit  : IStateMachineState
```

### 9.4 What `BaseStateMachineAndFirstStateContainerSetup.Register` Does

1. **Registers all states** — scans the assembly for every concrete `BaseStateMachineState<TStateMachine>` and registers it as `SingleInstance`.
2. **Registers all trigger command handlers** — for each `BaseTriggerCommand<TStateMachine>` found, creates and registers a `BaseTriggerCommandHandler<TTrigger, TStateMachine>` so the bus can route that trigger to the machine.
3. **Synthesises the init trigger** — dynamically emits a type `{Namespace}.Init{MachineName}Trigger` at runtime using `AssemblyBuilder`, registers it and its handler. This is the "fire to get started" message sent after `Build()`.
4. **Registers `StateMachineManager`** (once, shared across multiple machines in the same container).

### 9.5 What `Build` Does

```
Build(IContainer container)
  → Resolve StateMachineManager
  → Resolve BaseStateMachine
  → Register machine with manager
  → Configure initial state transition (InitTrigger → FirstState)
  → Lock TemporaryStateMachineHolder (thread-safe singleton handshake)
    → Resolve every IStateMachineState from the container
    → For each state: create StateWorker, call state.Configure(wrapper)
  → Optionally call stateMachine.SendInit() to fire the init trigger
```

The `TemporaryStateMachineHolder` lock is necessary because `BaseStateMachineState` constructors pull the current machine out of a thread-local singleton — a short-lived coupling that lets the constructor-injected state class reach its owning machine without a circular Autofac registration.

---

## 10. Cross-Machine Firing

A state in machine A can fire a trigger into machine B using `FireExternal`.

```csharp
// Inside a state of PumpStateMachine
protected bool FireExternal<TStateMachine, BaseTriggerCommandType>(
    BaseTriggerCommandType trigger,
    BusConfig? responseInfo = null)
    where TStateMachine          : IStateMachine
    where BaseTriggerCommandType : BaseTriggerCommand<TStateMachine>
```

**Example:**

```csharp
public class FaultState : BaseStateMachineState<PumpStateMachine>
{
    public override void OnEntry()
    {
        // Tell the alarm machine to start sounding
        FireExternal<AlarmStateMachine, ActivateAlarmTrigger>(
            new ActivateAlarmTrigger { Severity = AlarmSeverity.Critical });
    }
}
```

Both machines must be registered in the **same `IContainer`** and managed by the **same `StateMachineManager`**.

---

## 11. Publishing Events and Sending Commands

State classes have protected helpers for all outbound messaging.

### 11.1 Event Publishing

```csharp
// Publish a broadcast event (IEvent)
PublishEvent<TEvent>(TEvent ev)
```

The framework auto-discovers which events a state publishes by inspecting IL instructions at configure time (via Mono.Cecil). It finds all `PublishEvent<TEvent>(...)` or `SendResponse<TEvent>(...)` call sites inside the state class and calls `stateConfig.RegisterEvent(eventType)` for each, so the diagram generator can annotate the graph.

> **Events must be immutable.** An event is broadcast to every subscriber simultaneously. Mutating an event object after publishing it produces undefined behaviour. Use `init`-only properties or `record` types:
>
> ```csharp
> public record PumpStartedEvent(int Rpm)  : IEvent { }
> public record PumpStoppedEvent           : IEvent { }
> ```

```csharp
public class RunningState : BaseStateMachineState<PumpStateMachine>
{
    public override void OnEntry()
    {
        PublishEvent(new PumpStartedEvent { Rpm = _targetRpm });
    }

    public override void OnExit()
    {
        PublishEvent(new PumpStoppedEvent());
    }
}
```

### 11.2 Sending Commands

```csharp
// Route a point-to-point command
SendCommand<TCommand>(TCommand command, BusConfig? responseInfo = null)
```

### 11.3 Sending Responses

```csharp
// Reply to the BusConfig captured from the incoming trigger
SendResponse<TEvent>(TEvent ev, BusConfig responseInfo)
```

Use `GetCurrentResponseInfo()` to retrieve the `BusConfig` passed in with the current trigger.

### 11.4 Delayed Message Sender

```csharp
IDelayedMessageSender<TMessage>? sender = CreateDelayedMessageSender<TMessage>();
```

Returns a sender you can hold and invoke later, useful for timeouts or deferred actions.

---

## 12. Testing with `StateMachineForTest`

`StateMachineForTest` is a disposable test harness that runs an in-process, synchronous bus. It captures all commands and events, and provides trap/filter helpers for synchronization.

### 12.1 Basic Setup

```csharp
using StatePipes.StateMachine.Test;

public class PumpStateMachineTests : IDisposable
{
    private readonly StateMachineForTest _sut = new();

    public PumpStateMachineTests()
    {
        // Auto-discover first state:
        _sut.ConfigureStateMachine<PumpStateMachine>(new MyDummyRegistrar());

        // Or provide the first state explicitly:
        // _sut.ConfigureStateMachine<PumpStateMachine, IdleState>(new MyDummyRegistrar());
    }

    public void Dispose() => _sut.Dispose();
}
```

`IDummyDependencyRegistration` is your project-specific class that registers stubs/mocks for services the states depend on.

### 12.2 Generic Overloads of `ConfigureStateMachine`

```csharp
// Overload 1: auto-detect first state
void ConfigureStateMachine<StateMachineType>(
    IDummyDependencyRegistration dummyRegistrar,
    bool disableAutomaticMoveToState = true)
    where StateMachineType : IStateMachine

// Overload 2: explicit first state (useful when testing partial machines)
void ConfigureStateMachine<StateMachineType, NextStateAfterInitType>(
    IDummyDependencyRegistration dummyRegistrar,
    bool disableAutomaticMoveToState = true)
    where StateMachineType      : IStateMachine
    where NextStateAfterInitType: IStateMachineState
```

`disableAutomaticMoveToState = true` prevents the `MoveToState` internal trigger from firing automatically, so you can step through substates manually in tests.

### 12.3 Firing Triggers

```csharp
[Fact]
public void Start_Transitions_To_Running()
{
    _sut.Start();  // fires the init trigger, lands in IdleState

    _sut.Fire(new StartPumpTrigger { TargetRpm = 1200 });

    Assert.True(_sut.IsCurrentState<RunningState>());
}
```

### 12.4 Asserting on Commands and Events

```csharp
[Fact]
public void Starting_Publishes_PumpStartedEvent()
{
    _sut.Start();
    _sut.Fire(new StartPumpTrigger { TargetRpm = 1200 });

    var events = _sut.EventInvocations;
    Assert.Contains(events, e => e is PumpStartedEvent pse && pse.Rpm == 1200);
}
```

### 12.5 Trapping Async Commands with `TrapCommand`

When the state sends a command that triggers another async path, use `TrapCommand` to synchronise the test thread.

```csharp
[Fact]
public void FaultState_Sends_AlertCommand()
{
    _sut.Start();
    _sut.Fire(new StartPumpTrigger());

    // Set the trap before firing the trigger that causes the send
    using var trap = _sut.TrapCommand<AlertCommand>(skip: 0, timeoutMsec: 2000);

    _sut.Fire(new FaultDetectedTrigger { FaultCode = "OVERHEAT" });

    var cmd = trap.Wait();
    Assert.NotNull(cmd);
    Assert.IsType<AlertCommand>(cmd);
}
```

### 12.6 Filtering Commands

```csharp
// Block a command type from being processed (useful to pause the machine mid-flow)
_sut.FilterCommand<AlertCommand>(skip: 0, block: int.MaxValue);

// Remove the filter later
_sut.RemoveCommandFilter<AlertCommand>();
```

---

## 13. Generics Reference Card

| Type | Generic Parameters | Constraints | Purpose |
|------|--------------------|-------------|---------|
| `BaseTriggerCommand<TStateMachine>` | `TStateMachine` | `: IStateMachine` | Base for all triggers of a machine |
| `BaseStateMachineState<TStateMachine>` | `TStateMachine` | `: IStateMachine` | Base for all states of a machine |
| `ParentedBaseStateMachineState<TStateMachine, TParentState>` | `TStateMachine`, `TParentState` | `TStateMachine : IStateMachine`, `TParentState : BaseStateMachineState<TStateMachine>` | Base for child states (ensures parent is in same machine) |
| `StateConfigurationWrapper.Permit<TTrigger, TDest>()` | `TTrigger`, `TDest` | `TTrigger : ITrigger`, `TDest : IStateMachineState` | Declares an unconditional transition |
| `StateConfigurationWrapper.PermitIf<TTrigger, TDest>(guard)` | `TTrigger`, `TDest` | same as above | Declares a guarded transition |
| `StateConfigurationWrapper.PermitReentry<TTrigger>()` | `TTrigger` | `: ITrigger` | Re-enters same state unconditionally |
| `StateConfigurationWrapper.Ignore<TTrigger>()` | `TTrigger` | `: ITrigger` | Silently drops trigger in this state |
| `BaseTriggerCommandHandler<T, S>` | `T` (trigger), `S` (machine) | `T : BaseTriggerCommand<S>`, `S : IStateMachine` | Auto-registered bus handler that routes T into the machine |
| `StateMachineAndFirstStateContainerSetup<TStateMachine, TFirstState>` | `TStateMachine`, `TFirstState` | `TStateMachine : IStateMachine`, `TFirstState : IStateMachineState` | Container setup with explicit first state |
| `StateMachineContainerSetup<TStateMachine>` | `TStateMachine` | `: IStateMachine` | Container setup for test scenarios (no auto-init) |
| `IMessageHandler<TMessage>` | `TMessage` | `: class` | Bus handler contract |
| `TimedBlockOnFilter<A>` | `A` | (none) | Thread-synchronisation helper for tests |
| `BaseFilter<A>` | `A` | (none) | Abstract filter over a message category |

---

## 14. Attributes Reference Card

All guard attributes target `AttributeTargets.Method` and are applied to instance methods on a `BaseStateMachineState<T>` subclass.

| Attribute | Constructor Parameters | Method Signature Required | Behaviour |
|-----------|----------------------|--------------------------|-----------|
| `[PermitIf(typeof(TDest), "description?")]` | `Type destinationState`, `string? guardDescription` | `bool MethodName(TTrigger trigger, BusConfig? responseInfo)` | Permits transition to `TDest` when method returns `true` |
| `[IgnoreIf("description?")]` | `string? guardDescription` | `bool MethodName(TTrigger trigger, BusConfig? responseInfo)` | Ignores (drops) the trigger when method returns `true` |
| `[PermitReentryIf("description?")]` | `string? guardDescription` | `bool MethodName(TTrigger trigger, BusConfig? responseInfo)` | Returns `true` → re-enters same state (`OnExit`/`OnEntry` fire). Returns `false` → method body executes as a side effect but no state transition occurs. |

**The trigger type is inferred from the method's first parameter.** The framework reads `method.GetParameters()[0].ParameterType` — no generic parameter is needed on the attribute itself.

**Access modifiers**: Methods can be `private`, `protected`, or `public`; the reflection scan uses `BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance`.

---

## 15. End-to-End Example: Traffic Light

This self-contained example demonstrates every concept: machine, triggers, states, guards, substates, events, and tests.

### 15.1 State Machine Marker

```csharp
namespace TrafficLight
{
    public class TrafficLightMachine : IStateMachine { }
}
```

### 15.2 Triggers

```csharp
// Triggers/ — declared as records to enforce immutability
public record AdvanceTrigger      : BaseTriggerCommand<TrafficLightMachine>;
public record EmergencyOnTrigger  : BaseTriggerCommand<TrafficLightMachine>;
public record EmergencyOffTrigger : BaseTriggerCommand<TrafficLightMachine>;
```

### 15.3 Events

```csharp
// Events/ — declared as a record to enforce immutability
public record LightChangedEvent(string Color) : IEvent { }
```

### 15.4 States

```csharp
// States/GreenState.cs
public class GreenState
    : BaseStateMachineState<TrafficLightMachine>, IFirstStateForStateMachine
{
    [PermitIf(typeof(YellowState))]
    private bool OnAdvance(AdvanceTrigger trigger, BusConfig? responseInfo) => true;

    [PermitIf(typeof(EmergencyState))]
    private bool OnEmergencyOn(EmergencyOnTrigger trigger, BusConfig? responseInfo) => true;

    public override void OnEntry() => PublishEvent(new LightChangedEvent("Green"));
}
```

```csharp
// States/YellowState.cs
public class YellowState : BaseStateMachineState<TrafficLightMachine>
{
    private bool _isNightMode;

    [PermitIf(typeof(EmergencyState))]
    private bool OnEmergencyOn(EmergencyOnTrigger trigger, BusConfig? responseInfo) => true;

    [PermitIf(typeof(GreenState), "Night mode: skip red")]
    private bool ShouldSkipRed(AdvanceTrigger trigger, BusConfig? responseInfo)
        => _isNightMode;

    [PermitIf(typeof(RedState), "Normal mode: go to red")]
    private bool ShouldGoRed(AdvanceTrigger trigger, BusConfig? responseInfo)
        => !_isNightMode;

    public override void OnEntry() => PublishEvent(new LightChangedEvent("Yellow"));
}
```

```csharp
// States/RedState.cs
public class RedState : BaseStateMachineState<TrafficLightMachine>
{
    [PermitIf(typeof(GreenState))]
    private bool OnAdvance(AdvanceTrigger trigger, BusConfig? responseInfo) => true;

    [PermitIf(typeof(EmergencyState))]
    private bool OnEmergencyOn(EmergencyOnTrigger trigger, BusConfig? responseInfo) => true;

    public override void OnEntry() => PublishEvent(new LightChangedEvent("Red"));
}
```

```csharp
// States/EmergencyState.cs  (superstate — no transitions of its own)
public class EmergencyState : BaseStateMachineState<TrafficLightMachine> { }

// States/EmergencyFlashingState.cs  (first substate)
public class EmergencyFlashingState
    : ParentedBaseStateMachineState<TrafficLightMachine, EmergencyState>,
      IFirstSubstate
{
    // SubstateOf<EmergencyState> is registered automatically by the base class
    [PermitIf(typeof(GreenState))]
    private bool OnEmergencyOff(EmergencyOffTrigger trigger, BusConfig? responseInfo) => true;

    public override void OnEntry() => PublishEvent(new LightChangedEvent("Flashing"));
}
```

### 15.5 Container Setup

```csharp
public class TrafficLightContainerSetup : IContainerSetup
{
    private readonly BaseStateMachineContainerSetup _inner =
        new(typeof(TrafficLightMachine));

    public void Register(ContainerBuilder cb) => _inner.Register(cb);
    public void Build(IContainer c) => _inner.Build(c);
}
```

### 15.6 Tests

```csharp
public class TrafficLightTests : IDisposable
{
    private readonly StateMachineForTest _sut = new();

    public TrafficLightTests()
    {
        _sut.ConfigureStateMachine<TrafficLightMachine>(new NullDummyRegistrar());
        _sut.Start();  // machine lands in GreenState
    }

    [Fact]
    public void Initial_State_Is_Green()
        => Assert.True(_sut.IsCurrentState<GreenState>());

    [Fact]
    public void Advance_Goes_Green_To_Yellow()
    {
        _sut.Fire(new AdvanceTrigger());
        Assert.True(_sut.IsCurrentState<YellowState>());
    }

    [Fact]
    public void Advance_In_Normal_Mode_Goes_Yellow_To_Red()
    {
        _sut.Fire(new AdvanceTrigger());  // Green → Yellow
        _sut.Fire(new AdvanceTrigger());  // Yellow → Red (normal mode)
        Assert.True(_sut.IsCurrentState<RedState>());
    }

    [Fact]
    public void Emergency_On_From_Green_Goes_To_EmergencyFlashing()
    {
        _sut.Fire(new EmergencyOnTrigger());
        _sut.FireMoveToStateTrigger();  // enter the first substate
        Assert.True(_sut.IsCurrentState<EmergencyFlashingState>());
    }

    [Fact]
    public void Emergency_Off_Returns_To_Green()
    {
        _sut.Fire(new EmergencyOnTrigger());
        _sut.FireMoveToStateTrigger();
        _sut.Fire(new EmergencyOffTrigger());
        Assert.True(_sut.IsCurrentState<GreenState>());
    }

    [Fact]
    public void Entering_Green_Publishes_LightChangedEvent()
    {
        var events = _sut.EventInvocations;
        Assert.Contains(events, e => e is LightChangedEvent lce && lce.Color == "Green");
    }

    public void Dispose() => _sut.Dispose();
}
```

---

## Quick Reference: Composition Checklist

When creating a new state machine, work through this list in order:

- [ ] Create the `IStateMachine` marker class.
- [ ] Create at least one trigger inheriting `BaseTriggerCommand<TYourMachine>`.
- [ ] Create states inheriting `BaseStateMachineState<TYourMachine>`.
- [ ] Mark the entry state with `IFirstStateForStateMachine`.
- [ ] For each allowed transition, add a `[PermitIf(typeof(TDest))]` method returning `true`.
- [ ] For each trigger to silently drop in a state, add an `[IgnoreIf]` method returning `true`.
- [ ] For conditional transitions, replace the `=> true` body with real guard logic.
- [ ] Override `Configure` (calling `base.Configure(stateConfig)` first) only when `OnEntryFrom` or other non-attribute wiring is needed.
- [ ] For substates, inherit `ParentedBaseStateMachineState<TMachine, TParent>` and mark the first child with `IFirstSubstate`.
- [ ] Create a `BaseStateMachineContainerSetup` (or the explicit-first-state variant) and hook it into your service's `IContainerSetup`.
- [ ] Write tests using `StateMachineForTest`, call `Start()`, then `Fire(trigger)` to drive the machine.
