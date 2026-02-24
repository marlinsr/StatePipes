# State-Scoped Command Handling: A Framework Pattern for Incremental Development of Stateful Systems

**Shawn Marlin**
*StatePipes Open Source Project*
shawn.marlin@gmail.com

*Prepared with the assistance of Claude Code (Anthropic)*

---

## Abstract

Stateful systems — industrial controllers, robotic platforms, SCADA systems, and IoT devices — must respond to commands differently depending on their current operating state. Conventional practice implements this behavior through global command handlers that check the current state internally, typically using conditional dispatch logic. As fault-tolerance requirements are added, this logic expands to cover fault detection, recovery, and degraded operation paths, entangling them with the original business logic and producing code that is difficult to reason about, test, and extend.

This paper describes the *State-Scoped Command Handler* pattern, in which command handlers are defined as methods of individual state classes rather than as global dispatchers. The currently active state instance is solely responsible for handling the commands it declares; the framework discovers and routes commands automatically with no manual registration. Commands that arrive in a state with no declared handler are naturally excluded. The pattern enables an incremental development workflow in which the happy-path operational logic is implemented and validated first, and fault-tolerance states are introduced later as new classes without modifying existing code. We describe the pattern in structured form, characterize the forces it resolves, and contrast it with the closest prior work including the GoF State pattern, the P programming language, Akka FSM, and Erlang's `gen_statem`. A reference implementation in C# demonstrates the pattern for industrial control applications and is available as an open-source framework.

---

## 1. Introduction

### 1.1 The Problem

Consider an industrial pump controller. In normal operation, the controller receives a *start* command and transitions to a running state; a *stop* command returns it to idle. This happy-path logic is straightforward to implement. The challenge emerges when the system must also handle what happens when a sensor fault is detected during operation, when a start command arrives while the pump is already faulted, when a network timeout occurs mid-recovery, or when an emergency stop pre-empts any other state. Each of these scenarios is not an edge case — in SCADA, robotics, and industrial IoT, fault detection, recovery sequencing, and degraded-mode operation are first-class requirements.

The conventional approach is to implement command handling through global dispatchers: for each command type, a single handler receives the command and uses conditional logic — switch statements, if-else chains, or flag checks — to determine the correct response based on the current system state. Listing 1 illustrates this structure.

**Listing 1 — Conventional global handler**
```csharp
class StartCommandHandler {
    void Handle(StartCommand cmd) {
        switch (currentState) {
            case Idle:       transitionTo(Running); break;
            case Running:    reject(cmd);           break;
            case Fault:      reject(cmd);           break;
            case Recovering: defer(cmd);            break;
        }
    }
}
```

This structure has a well-known failure mode: as the number of states grows, every handler must be updated to account for every new state. When fault handling is added to a system that was initially designed for the happy path, the developer must locate every existing handler and insert logic for the fault and recovery cases. This work is repetitive, error-prone, and violates the open/closed principle [Meyer 1988]: adding a new state requires modifying existing code. The result is that fault-handling logic becomes entangled with business logic across many files — what practitioners commonly call spaghetti code.

This problem is not unique to a single language or platform. It recurs wherever complex, state-dependent systems are implemented: robotic arm controllers, network protocol stacks, industrial automation pipelines, vehicle control units, and increasingly, the state machines that structure AI agent behavior.

### 1.2 The Key Insight

The root cause is a structural inversion. In the conventional model, a handler owns the dispatch logic and checks the state. We propose inverting this relationship: *the state owns the handler declarations*. Each state class declares exactly which commands it handles, as methods on that class. The framework discovers these declarations and routes each incoming command to the method on the currently active state instance. No delegation glue is written by the developer; no handler needs to know about any state other than its own.

Listing 2 illustrates the resulting structure for the same pump controller.

**Listing 2 — State-scoped command handlers**
```csharp
class IdleState : BaseStateMachineState<PumpMachine> {
    [PermitIf(typeof(RunningState))]
    bool OnStart(StartCommand cmd, ...) => true;

    [IgnoreIf]
    bool OnStop(StopCommand cmd, ...) => true;
}

class RunningState : BaseStateMachineState<PumpMachine> {
    [PermitIf(typeof(IdleState))]
    bool OnStop(StopCommand cmd, ...) => true;

    [PermitIf(typeof(FaultState))]
    bool OnFaultDetected(FaultCommand cmd, ...) => true;
}

class FaultState : BaseStateMachineState<PumpMachine> {
    [PermitIf(typeof(RecoveringState))]
    bool OnRecover(RecoverCommand cmd, ...) => true;
}
```

Each state class is complete and self-contained. `IdleState` contains everything that happens when the system is idle; `RunningState` contains everything that happens when the system is running. The `FaultState` and `RecoveringState` classes can be written and tested entirely independently of the happy-path states. Adding them requires no modification to `IdleState` or `RunningState`.

This inversion has a direct consequence for development practice: the happy-path states can be implemented, tested, and validated as a complete and correct artifact. Fault-tolerance states are added incrementally, one at a time, each as a new class. The system remains correct at every intermediate step because existing states are never modified. We call this property *incremental correctness extension* and argue that it is a first-class benefit of the pattern.

### 1.3 Contributions

This paper makes the following contributions:

1. **The State-Scoped Command Handler pattern** (Section 3), described in structured pattern form with problem, context, forces, solution, and consequences. The pattern is the first formalization of state-scoped command dispatch as a reusable OOP framework pattern in a mainstream host language, as distinct from standalone DSLs (P language [Desai et al. 2013]), actor framework APIs (Akka FSM), and embedded function-pointer models (Samek/QP [Samek 2008]).

2. **The incremental correctness extension claim** (Section 4), which articulates and formalizes the development workflow benefit: happy-path states first, fault-tolerance states added without modifying existing code. To our knowledge this property has not been previously identified or formalized as a consequence of state machine composition strategy.

3. **A characterization of the pattern's relationship to AI-assisted development** (Section 5), showing that the structural isomorphism between natural language state machine descriptions and state class declarations reduces translation errors in both human- and AI-assisted code generation workflows.

4. **A reference implementation** (Section 6) in the form of the StatePipes framework for C#/.NET, with application to SCADA, robotic, and IoT systems. The framework provides automatic handler discovery via reflection, compile-time type safety through generic constraints, and diagram generation directly from code.

### 1.4 Paper Organization

Section 2 surveys related work, including the GoF State pattern, typestate programming, the P language, Akka FSM, Erlang `gen_statem`, Samek's QP framework, and XState. Section 3 presents the State-Scoped Command Handler pattern in structured form. Section 4 formalizes and evaluates the incremental correctness extension claim. Section 5 examines implications for AI-assisted development. Section 6 describes the StatePipes reference implementation and industrial case studies. Section 7 discusses limitations and future work, and Section 8 concludes.

---

## 2. Related Work

The concept that valid operations on a system depend on its current state has been recognized across several research traditions, including formal methods, programming language theory, object-oriented design patterns, actor-based frameworks, and domain-specific languages for reactive systems. We survey each in turn and identify the gap that the state-scoped command handler pattern addresses.

### 2.1 Theoretical Foundations: Statecharts and Hierarchical State Machines

The theoretical foundation for hierarchical, concurrent state machines was established by Harel [1987], who extended classical finite automata with three key ideas: hierarchy (substates that inherit transitions from their parent), concurrency (orthogonal regions executing simultaneously), and communication (broadcast events crossing region boundaries). Harel's statecharts became the basis for UML behavioral state machines [OMG 2003], the W3C SCXML standard [W3C 2015], and virtually every modern state machine tool and library.

Harel's formalism establishes that each state in a machine defines the set of events to which it responds and the actions those events produce — a direct precursor to the idea that handlers are state-scoped. However, statecharts are a *visual formalism*: transitions and their associated actions are arcs in a diagram, not methods in a class. The question of how statechart semantics should be mapped to code organization, and specifically who owns the handler for a given event in a given state, is outside the scope of Harel's original contribution.

UML 2.0 Protocol State Machines [OMG 2003] take one step toward the code organization question by specifying, at the modeling level, which operations on a classifier are valid in which state. A protocol state machine is a contract: calling `open()` on a `Connection` in the `Closed` state is valid; calling it in the `Open` state is not. This is a specification mechanism, not a dispatch mechanism — enforcement is intended for static analysis and documentation rather than runtime routing. Crucially, the handler implementation remains in the classifier, not in the state.

### 2.2 Object-Oriented Design Patterns

The GoF State pattern [Gamma et al. 1994] is the most structurally proximate classical design pattern. It places state-specific behavior inside concrete `State` subclasses: the `Context` object holds a reference to the current `State` instance and delegates method calls to it. When the state changes, the `Context` swaps its reference, and subsequent delegated calls invoke the new state's implementations. This establishes the essential mechanism — dispatch flows through the active state object — and is the structural predecessor of state-scoped command handling.

The GoF pattern, however, operates on direct method invocations with a fixed interface: all state classes implement the same set of methods, even those for which a given state has no meaningful behavior. There is no concept of an external *command* arriving from outside the object, no framework-managed routing infrastructure, and no natural representation of "this command does not apply in this state." The developer is responsible for writing the delegation glue inside the `Context`.

Dyson and Anderson [1997] extended the GoF pattern with seven refinements addressing transition ownership and state member allocation. Adamczyk [2003] provides a comprehensive catalog of finite state machine patterns in OOP. Neither work introduces the concept of a framework that automatically discovers and routes typed external commands to the currently active state.

### 2.3 Typestate and Type-Theoretic Approaches

Strom and Yemini [1986] introduced *typestate*, the concept that the set of legal operations on an object depends not only on its declared type but on its current runtime state. Aldrich et al. [2009] substantially revived and extended typestate in the Plaid programming language, where each state defines its own interface and method implementations. The formal foundations of typestate-oriented programming were established by Garcia et al. [2014].

The distinction from state-scoped command handling is fundamental. Typestate addresses *internal* method invocations within a single type system: the compiler knows the state of the object at the call site and enforces legality statically. State-scoped command handling addresses *external* messages arriving at runtime from sources that do not share the state machine's type information — network packets, sensor readings, operator commands, inter-service messages. The two approaches address complementary problems and are not in competition.

### 2.4 Domain-Specific Languages for State-Driven Systems

The P programming language [Desai et al. 2013], developed at Microsoft Research, is the prior work most closely related to the state-scoped command handler pattern. P is a domain-specific language for specifying and verifying asynchronous, event-driven state machines. A P state machine is defined as a collection of *state blocks*, each containing `on Event do { handler }` declarations. The P runtime routes incoming events to the current state's declared handler; an event arriving in a state with no matching `on` declaration is rejected or silently dropped. P has been applied to device driver verification at Microsoft and to distributed protocol specification at AWS.

The structural parallel is direct and strong: handler code is scoped to the state, the framework performs routing, and unhandled events are naturally excluded. The principal distinction is that P is a *standalone language* requiring its own toolchain, compiler, and runtime. The state-scoped command handler pattern, by contrast, is a *framework design pattern* expressed within a mainstream OOP language, making the benefits available without toolchain changes, with full access to existing libraries, IDE tooling, dependency injection, and host-language type systems.

Erlang's `gen_statem` behaviour [Ericsson 2016], in `state_functions` callback mode, provides per-state function dispatch as a language primitive: each state is represented by a module function named after the state atom, and the OTP runtime routes incoming events to the current state function. This is functionally equivalent to state-scoped command handling, realized in a functional style, but does not offer class-based encapsulation, typed command hierarchies, or attribute-driven declaration.

### 2.5 Actor Frameworks

Akka's Classic FSM API [Lightbend] provides state-scoped message handling through a `when(StateName) { case Event(message, data) => ... }` DSL. All `when` blocks are registered during actor initialization in a single configuration pass; the framework dispatches incoming messages to the matching `when` block for the current state. Akka Typed [Lightbend] replaces the FSM DSL with behavior functions: each state is a `Behavior[Command]` value — a function from (context, command) to the next behavior.

Both Akka variants differ from the state-scoped command handler pattern in a structurally important way: all state-to-handler mappings are configured at initialization or expressed through ad hoc pattern matching. There is no mechanism for *automatic discovery* of which handlers a state declares. Additionally, Akka operates within the actor model's concurrency and fault-supervision semantics, which carry significant overhead for systems that do not require distributed actors.

### 2.6 Embedded and Real-Time Frameworks

Miro Samek's QP framework [Samek 2008] implements hierarchical state machines in C and C++ for embedded systems. Each state is represented as a function that receives an event signal and returns a disposition (handled, ignored, or deferred). The QP dispatcher invokes the current state function with each incoming event. This is architecturally equivalent to state-scoped command handling, but uses integer signal constants rather than typed command objects and is designed for hard real-time embedded constraints.

ROOM (Real-Time Object-Oriented Modeling) [Selic et al. 1994] introduced actor-like capsules with ROOMchart-based behaviors for real-time embedded systems. ROOM's ROOMcharts define, for each state, which incoming messages trigger which transitions — functionally equivalent to per-state handler definition — but expressed entirely in a graphical modeling notation with code generation, not as a developer-facing framework API.

### 2.7 Modern State Machine Libraries

XState [Khourshid 2016] is a widely adopted JavaScript/TypeScript library implementing full statechart semantics. Machines are defined as data-driven configuration objects: each state node has an `on` property mapping event names to transitions, making the handler-per-state relationship explicit. XState is data-driven rather than class-driven: the handler is a function in a configuration object, not a method declared inside a state class, and there is no automatic handler discovery via type inspection.

### 2.8 Summary of the Gap

Across the prior art, state-scoped handling of external commands appears in three forms: as a *type-theoretic enforcement mechanism* for internal method calls (Typestate, Plaid); as a *standalone language primitive* in DSLs (P, Erlang gen_statem); and as a *framework DSL or configuration object* in actor and state machine libraries (Akka FSM, XState, QP). No prior work formalizes state-scoped command handling as a *reusable OOP framework pattern* in which:

1. Each state is a host-language class whose *methods* declare command handling behavior, using the class mechanism for encapsulation, inheritance, and polymorphism.
2. The framework performs *automatic discovery* of handler declarations at configuration time via type inspection, requiring no manual handler registration by the developer.
3. *Typed command objects* serve as the unit of dispatch, enabling compile-time detection of routing errors through the host language's generic type system.
4. Commands with no declared handler in the active state are *naturally excluded* by the absence of a handler declaration, without explicit ignore lists.
5. The pattern explicitly enables an *incremental development workflow* in which happy-path state logic is implemented and validated first, and fault-tolerance states are added later as new classes without modifying existing state logic.

The fifth point has not been articulated in the prior literature as a first-class benefit of state machine composition strategies. The claim that the pattern itself supports an incremental development methodology — specifically that fault states are structurally isolated from happy-path states — is a distinct contribution that warrants empirical evaluation.

---

## 3. The State-Scoped Command Handler Pattern

This section presents the pattern in structured form following the Alexandrian pattern format [Alexander et al. 1977] as adapted for software patterns [Gamma et al. 1994; Coplien and Schmidt 1995].

### 3.1 Name

**State-Scoped Command Handler**

### 3.2 Also Known As

Per-State Message Handler; State-Local Command Dispatch; State-Owned Handler.

### 3.3 Context

You are building a software system whose behavior is governed by an explicit state machine. The system receives commands — messages that arrive at runtime from external sources such as message queues, sensor inputs, operator interfaces, inter-service calls, or hardware interrupts. The correct response to any given command depends on the system's current state: the same command may cause a transition in one state, be silently ignored in another, and be deferred or rejected in a third. The state space is non-trivial — typically three or more states, frequently involving hierarchical substates for operational modes, fault conditions, and recovery sequences.

The system is developed incrementally. Initial development focuses on the operational happy path. Fault detection, degraded operation, and recovery behavior are subsequently layered in as requirements are refined through testing and field operation. The system will be maintained and extended over its lifetime; new states will be added as requirements evolve.

### 3.4 Problem

**How should command handling logic be organized in a stateful system such that each state's behavior is comprehensible in isolation, new states can be added without modifying existing state logic, and commands arriving in inapplicable states are handled correctly without exhaustive explicit enumeration?**

### 3.5 Forces

**Force 1 — Locality vs. Completeness.** Placing all handling for a given command type in one location (a global handler) makes it easy to find everything a command does, but distributes a state's complete behavior across many files. Placing all handling for a given state in one location (a state class) makes that state's behavior comprehensible in isolation, but distributes a command's handling across many classes. In systems where states are the primary unit of comprehension and change — as in industrial control, robotics, and protocol implementation — state-local organization is preferable.

**Force 2 — Extensibility vs. Modification.** Adding a new state to a system with global handlers requires visiting every handler that could receive commands in that state and inserting branches. This is a modification of existing, tested code — a violation of the open/closed principle [Meyer 1988] and a source of regression risk. State-local organization makes state addition additive: a new state class is created, existing classes are unchanged. Adding a new command type requires adding a handler method to every responsive state — the symmetrical cost.

**Force 3 — Natural Exclusion vs. Explicit Enumeration.** In a global handler, the developer must explicitly enumerate all states in which a command should be ignored or rejected. Omitting a state typically produces silent incorrect behavior — the most dangerous defect form in a stateful system. In a state-local organization, the *absence* of a handler declaration is itself a complete specification.

**Force 4 — Happy-Path Priority vs. Fault-Tolerance Coverage.** Systems are most reliably developed by implementing and validating the primary operational path first. Any organizational strategy that requires developers to consider all possible states simultaneously creates pressure to implement incomplete logic early. State-local organization allows the happy-path states to constitute a complete, valid, testable system. Fault states are genuinely additive.

**Force 5 — Framework Overhead vs. Discovery Transparency.** Automatic handler discovery reduces boilerplate but introduces a routing path that is not visible in the calling code. The primary mitigation is the auto-generated state machine diagram: because the framework derives the complete routing structure from attribute declarations, it can emit an always-accurate diagram showing every state, every valid command transition, every guard description, and every published event. A developer who wants to understand what happens when command X arrives in state Y reads the diagram rather than tracing through reflection machinery. This is a stronger transparency mechanism than documentation or IDE tooling alone, because the diagram cannot diverge from the implementation.

**Force 6 — Compile-Time Safety vs. Runtime Flexibility.** Strongly typed command objects enable compile-time detection of routing errors. Untyped string-keyed dispatch is more flexible but defers all routing errors to runtime.

### 3.6 Solution

**Define each state as an independent class. Within that class, declare command handlers as methods whose parameter type identifies the command they handle. A framework layer discovers these handler declarations at configuration time through type inspection, and routes each incoming command to the method on the currently active state instance whose parameter type matches. The developer writes no routing logic.**

The solution has three structural elements:

**Element 1 — State Classes.** Each state inherits from a common base class parameterized by a machine marker type. The machine marker is an empty class serving as a compile-time key that binds all states and commands belonging to the same machine. A state class declares its command handling behavior as methods annotated with handler attributes.

**Element 2 — Typed Commands.** Each command is a typed, immutable object. Commands inherit from a base class parameterized by the same machine marker. The generic constraint ensures commands are statically associated with a specific machine.

**Element 3 — Framework Router.** A framework-managed router is automatically instantiated for each command type at container setup time. Upon receiving a command, it invokes `Fire` on the machine with the command as payload. The machine delegates to the current state instance, which invokes the matching handler method. No developer-authored routing code exists.

Handler declaration attributes carry three semantics:

- **Transition** (`[PermitIf(typeof(DestinationState))]`): if the guard method returns `true`, the machine transitions to the destination state, invoking `OnExit` then `OnEntry`. If it returns `false`, the command is consumed without effect.

- **Ignore** (`[IgnoreIf]`): the command is silently consumed. No transition occurs.

- **In-place processing** (`[PermitReentryIf]` returning `false`): the handler method body executes — publishing an event, updating internal state, or sending a response — but no state transition occurs and `OnExit`/`OnEntry` are not invoked.

A fourth value — `[PermitReentryIf]` returning `true` — re-enters the current state, firing `OnExit` then `OnEntry`.

### 3.7 Structure

```
IStateMachine                             (empty marker; generic type key)
    └── PumpMachine : IStateMachine       (developer-defined)

IStateMachineState
    └── BaseStateMachineState<TMachine>   (base for all states; owns Configure() scan)
        ├── IdleState
        ├── RunningState
        ├── FaultState
        └── ParentedBaseStateMachineState<TMachine, TParent>
                └── RecoveringState       (substate)

ITrigger : ICommand : IMessage
    └── BaseTriggerCommand<TMachine>
        ├── StartCommand
        ├── StopCommand
        └── FaultDetectedCommand

IMessageHandler<TCommand>
    └── BaseTriggerCommandHandler<T, S>   (framework router; one per command type)

StateMachineManager                       (registry)
StateConfigurationWrapper                 (fluent builder)
[PermitIf] / [IgnoreIf] / [PermitReentryIf]  (handler declaration attributes)
```

Generic constraints enforce correct composition at compile time: a command must target a valid machine; a state must belong to a valid machine; a substate's parent must belong to the same machine; a router is constrained to command types belonging to its machine.

### 3.8 Dynamics

```
External source
    │
    ▼
Message Bus (IStatePipesService)
    │  Routes by message type to registered IMessageHandler<T>
    ▼
BaseTriggerCommandHandler<StartCommand, PumpMachine>
    │  Calls: stateMachine.Fire(startCommand, responseInfo)
    ▼
BaseStateMachine
    │  Invokes guard closure for StartCommand on current state
    ▼
IdleState.OnStart(StartCommand cmd, BusConfig? responseInfo)
    │
    ├── returns true  → IdleState.OnExit() → RunningState.OnEntry()
    └── returns false → command consumed; no transition
```

For `[IgnoreIf]`: command consumed silently if guard returns `true`. For `[PermitReentryIf]` returning `false`: method body executes as a side effect; `OnExit`/`OnEntry` are not called. For a command with no matching handler: the framework raises a detectable runtime exception — not a silent failure.

### 3.9 Implementation

**Step 1 — Define the machine marker.**
```csharp
public class PumpMachine : IStateMachine { }
```

**Step 2 — Define commands as typed, immutable objects.**
```csharp
public record StartCommand(int TargetRpm)       : BaseTriggerCommand<PumpMachine>;
public record StopCommand                        : BaseTriggerCommand<PumpMachine>;
public record FaultDetectedCommand(string Code)  : BaseTriggerCommand<PumpMachine>;
```

**Step 3 — Implement happy-path states first.** Mark the entry state with `IFirstStateForStateMachine`.
```csharp
public class IdleState
    : BaseStateMachineState<PumpMachine>, IFirstStateForStateMachine
{
    [PermitIf(typeof(RunningState))]
    private bool OnStart(StartCommand cmd, BusConfig? r) => true;

    [IgnoreIf]
    private bool OnStop(StopCommand cmd, BusConfig? r) => true;
}

public class RunningState : BaseStateMachineState<PumpMachine>
{
    [PermitIf(typeof(IdleState))]
    private bool OnStop(StopCommand cmd, BusConfig? r) => true;

    public override void OnEntry()
    {
        var cmd = GetCurrentTrigger<StartCommand>();
        PublishEvent(new PumpStartedEvent(cmd?.TargetRpm ?? 0));
    }
}
```

At this point the system is complete and deployable with respect to happy-path requirements. `IdleState` and `RunningState` have no knowledge of fault states.

**Step 4 — Add fault-tolerance states incrementally.** Each new state is a new class. No existing class is modified except to add a single entry-point method.
```csharp
public class FaultState : BaseStateMachineState<PumpMachine>
{
    [PermitIf(typeof(RecoveringState))]
    private bool OnRecover(RecoverCommand cmd, BusConfig? r) => true;

    [IgnoreIf]
    private bool OnStart(StartCommand cmd, BusConfig? r) => true;

    [IgnoreIf]
    private bool OnStop(StopCommand cmd, BusConfig? r) => true;

    public override void OnEntry()
    {
        var fault = GetCurrentTrigger<FaultDetectedCommand>();
        PublishEvent(new FaultOccurredEvent(fault?.Code ?? "UNKNOWN"));
    }
}

// The only modification to RunningState — one new method:
[PermitIf(typeof(FaultState))]
private bool OnFault(FaultDetectedCommand cmd, BusConfig? r) => true;
```

**Step 5 — Register the machine.**
```csharp
public class PumpServiceContainerSetup : IContainerSetup
{
    private readonly BaseStateMachineContainerSetup _inner = new(typeof(PumpMachine));
    public void Register(ContainerBuilder cb) => _inner.Register(cb);
    public void Build(IContainer c) => _inner.Build(c);
}
```

**Cross-cutting commands** that apply in every state can be declared once on a superstate; all substates inherit the transition without redeclaration.

### 3.10 Resulting Context

#### Benefits

**B1 — State cohesion.** All behavior for a state is co-located in a single class. A developer reading a state class has a complete description of the system's behavior in that state.

**B2 — Additive extensibility.** New states are new classes. Existing state classes are not modified when a new state is added. The open/closed principle [Meyer 1988] is satisfied at the state level.

**B3 — Natural exclusion of invalid commands.** The absence of a handler declaration is itself a complete specification. The framework detects and reports unhandled commands.

**B4 — Structural testability.** Each state class is independently testable. A test can place the machine in a specific state, fire a command, and assert on the resulting state and published events in isolation.

**B5 — Diagram accuracy.** The complete transition structure is declared as attributes; the framework can generate a correct state diagram from the assembly at any time. The diagram is never stale.

**B6 — Incremental correctness extension.** The pattern enables a development workflow in which the system is correct and deployable after each incremental addition. Section 4 formalizes this property.

#### Liabilities

**L1 — Framework dependency.** The pattern requires a framework providing routing infrastructure, automatic handler discovery, and a state machine engine.

**L2 — Dispatch opacity.** The routing path is derived by the framework from type declarations rather than visible in calling code. Mitigated by IDE tooling and framework documentation.

**L3 — Cross-cutting command verbosity.** A command handled identically in many (but not all) states requires a declaration in each, or factoring into a superstate. Neither is without cost.

**L4 — Command addition cost.** Adding a new command type requires adding handler declarations to every state that should respond to it — up to N modifications for N states. This is the symmetrical cost to B2 and favors systems with a stable command vocabulary.

### 3.11 Known Uses

**StatePipes** (this paper's reference implementation): deployed in industrial automation systems; intialization, fault, and recovery states constitute the majority of total states.

**P Language** [Desai et al. 2013]: per-state `on Event do { handler }` declarations; applied to Windows device driver verification and AWS distributed protocols.

**Erlang gen_statem** [Ericsson 2016]: per-state function dispatch in `state_functions` mode; idiomatic to the OTP process model.

**Akka FSM** [Lightbend]: `when(StateName)` DSL wires handlers per state at actor initialization. Structurally equivalent dispatch; differs in DSL-level rather than class-level declaration.

### 3.12 Related Patterns

**GoF State Pattern** [Gamma et al. 1994]: the direct structural ancestor. Extended here with a command abstraction, framework-managed routing, and automatic handler discovery.

**GoF Command Pattern** [Gamma et al. 1994]: commands as first-class typed objects. Complementary: Command describes representation; State-Scoped Command Handler describes routing and scoping.

**GoF Chain of Responsibility** [Gamma et al. 1994]: appropriate when the routing key is not known at configuration time or when multiple handlers may process a command sequentially. State-Scoped Command Handler is appropriate when the routing key is the current state — precisely known at runtime.

**GoF Observer Pattern** [Gamma et al. 1994]: events published by states follow the Observer pattern. Governs outbound event notification; complementary to the inbound command routing of this pattern.

**Typestate** [Strom and Yemini 1986; Aldrich et al. 2009]: enforces state-dependent operation validity statically. Complementary: typestate is appropriate for method calls within a closed type system; state-scoped command handling is appropriate for external runtime messages.

---

## 4. Incremental Correctness Extension

Section 1 introduced the claim that the pattern enables a development workflow in which happy-path operational logic is implemented and validated first, and fault-tolerance states are added later without modifying existing code. This section formalizes that claim, provides a structural argument for why it holds, introduces a measurable metric, and demonstrates the argument through a worked example.

### 4.1 Definitions

We model a state machine as a tuple M = (S, C, δ, s₀) where:

- **S** is a finite set of states.
- **C** is a finite set of command types.
- **δ: S × C → S ∪ {⊥, ⊙}** is the transition function. δ(s, c) = s' means command `c` in state `s` transitions to s'. δ(s, c) = ⊥ indicates unhandled. δ(s, c) = ⊙ indicates silently ignored.
- **s₀ ∈ S** is the initial state.

A **trace** of M is a sequence (s₀, c₁, s₁, c₂, s₂, …) where each sᵢ₊₁ = δ(sᵢ, cᵢ₊₁) and no step is ⊥. We write Tr(M) for the set of all valid traces. A **specification** Φ is a set of traces; M ⊨ Φ if Tr(M) ⊆ Φ.

**Definition 1 (Happy-Path Submachine).** M_hp = (S_hp, C, δ_hp, s₀) where S_hp ⊂ S is the set of operational states and δ_hp is δ restricted to S_hp.

**Definition 2 (State Extension).** M' = (S', C', δ', s₀) is a *state extension* of M_hp if:

1. S_hp ⊆ S'.
2. S_new = S' \ S_hp ≠ ∅.
3. For all s ∈ S_hp and c ∈ C: if δ_hp(s, c) ∈ S_hp ∪ {⊙}, then δ'(s, c) = δ_hp(s, c). Only commands previously *unhandled* (⊥) in existing states may be given new transitions into S_new.
4. C' ⊇ C.

Condition 3 is the critical constraint: existing handled transitions are preserved unchanged.

**Definition 3 (Projection).** M'|_{S_hp} denotes M' restricted to traces remaining within S_hp.

### 4.2 The Incremental Correctness Extension Property

**Theorem (Incremental Correctness Extension).** *Let M_hp ⊨ Φ_hp. Let M' be any state extension of M_hp. Then M'|_{S_hp} ⊨ Φ_hp.*

**Proof.** By Definition 2 (condition 3), for all s ∈ S_hp and c ∈ C: if δ_hp(s, c) ∈ S_hp ∪ {⊙}, then δ'(s, c) = δ_hp(s, c). Consequently, any trace of M' remaining within S_hp follows the same transition sequence as the corresponding trace of M_hp. Therefore Tr(M'|_{S_hp}) = Tr(M_hp) and M'|_{S_hp} ⊨ Φ_hp. □

**Corollary (Composability of Correctness).** Fault-tolerance coverage can be added iteratively. Each state extension preserves the correctness of all previously validated states at every intermediate development step.

### 4.3 Why the Property Holds in the State-Scoped Command Handler Pattern

In the **State-Scoped Command Handler pattern**, each state's transitions are declared as methods on that state's class. A state extension requires: (1) creating new state class files for S_new, and (2) adding new handler methods to existing state classes for each entry point into S_new. These are *additions* of new methods, not *modifications* of existing methods. Modifying an existing transition would require editing an existing method body — a deliberate act distinguishable from the additive act of adding a new method, and visible in any code review diff.

In a **global handler architecture**, adding S_new requires inserting new branches into existing handler method bodies — *modifications* of existing code. Missed branches are silent failures: if `StartCommandHandler.Handle()` does not add a branch for `FaultState`, the machine executes incorrect behavior when a `Start` command arrives while faulted, and no structural property signals the omission.

### 4.4 The State Extension Modification Count Metric

**Definition 4 (State Extension Modification Count, SEMC).** Given architecture A and a state extension adding S_new, SEMC(A, S_new) is the number of existing source files that must be *modified* (not added) to correctly implement the extension.

SEMC isolates regression risk: modified files may introduce regressions; added files cannot.

For the **State-Scoped Command Handler pattern**:
```
SEMC_scoped(S_new) = |{s ∈ S_hp : ∃ c ∈ C such that δ'(s, c) ∈ S_new}|
```
This is the number of existing states with at least one transition entering the new state.

For a **global handler architecture**:
```
SEMC_global(S_new) = |{c ∈ C : ∃ s ∈ S_new such that c may arrive}|
```
This is the number of command types that may arrive while in any new state.

| Architecture | SEMC per new state | Grows with |
|---|---|---|
| Global handler | O(\|C\|) | Command vocabulary size |
| State-scoped | O(\|S_entry\|) | Number of entry points into new state |

For systems in the pattern's target domain, the command vocabulary is characteristically larger than the number of entry points into any given fault state. A pump controller may have twelve command types but only one state from which a specific fault is reachable.

### 4.5 Worked Example: Industrial Pump Controller

| Extension Step | New Files | SEMC_scoped | SEMC_global (equivalent) |
|---|---|---|---|
| Phase 1: Happy-path (Idle, Running) | 5 | 0 | 0 |
| Phase 2: Add FaultState | 2 | 1 | 3 |
| Phase 3: Add RecoveringState | 2 | 1 | 4 |
| Phase 4: Add EmergencyState | 2 | 3 | 5 |
| **Total modifications to existing files** | | **5** | **12** |

Over four development phases, the state-scoped architecture required 5 modifications to existing files; the global handler architecture required 12. More importantly, 10 of the 12 global handler modifications are insertions of new branches into existing routing logic — each a regression risk point. All 5 of the state-scoped modifications are additions of new methods to existing classes — structurally isolated from existing behavior.

### 4.6 Scope and Limitations

The theorem holds under the extension rule (Definition 2, condition 3). The pattern makes violations structurally visible but does not prevent them. SEMC measures modification *count*, not modification *risk*. The property addresses *structural* correctness — preservation of transition behavior — not semantic correctness. It applies cleanly when new states are entered from previously ⊥ transitions. Redesign scenarios that require changing an existing transition fall outside the incremental workflow and require explicit re-validation.

---

## 5. Implications for AI-Assisted Development

The previous sections describe the pattern in terms of human developer practice. This section examines an increasingly significant secondary implication: the pattern is structurally well-suited to AI-assisted code generation, and the reasons illuminate something deeper about the relationship between code structure and the fidelity of machine-generated software.

### 5.1 The Specification-to-Implementation Translation Problem

AI code generation systems work by translating intent expressed in natural language into executable code. The quality of generated output depends substantially on the *semantic distance* between the natural language description and the target code structure [Hindle et al. 2012]. For stateful systems, natural language specifications tend to follow a consistent structure: subject (current state), trigger (incoming command), condition (when applicable), outcome (next state or action). Consider:

> "When the pump is *idle* and receives a *start* command, transition to *running*."

This maps almost directly to:
```csharp
// In IdleState:
[PermitIf(typeof(RunningState))]
bool OnStart(StartCommand cmd, ...) => true;
```

The subject identifies the state class. The trigger identifies the method's parameter type. The condition becomes the method body. The outcome is encoded in the attribute. This near-bijective mapping is not a coincidence of surface syntax; it reflects that the pattern's structure is isomorphic to the structure of state machine specifications as they naturally occur in requirements documents and domain expert descriptions.

In a conventional global handler architecture, the AI must perform an *architectural inversion* — choosing where to embed state-checking logic and how to group branches — before generating any code. This additional creative step is a source of inconsistency across generated output.

### 5.2 The Scaffold Effect on Generation Reliability

AI code generation quality degrades in the presence of architectural ambiguity [Chen et al. 2021; Austin et al. 2021]. The State-Scoped Command Handler pattern eliminates this ambiguity: there is exactly one way to express a state, one way to express a command handler in a state, one way to express a transition. The rigid structural scaffold reduces the likelihood of structural variation in AI output.

Additionally, the host language's generic type system acts as a post-generation validation layer: the compiler rejects generated code in which a trigger does not belong to the correct machine, a destination state is not a valid state type, or a handler method has an incorrect signature. This provides immediate, actionable feedback for correction without requiring test execution — a property not shared by string-keyed dispatch frameworks such as XState or Akka FSM.

### 5.3 Incremental Generation as a First-Class Workflow

Human developers using AI assistance most productively work in short, independently reviewable increments [Ziegler et al. 2022]. Because each state class is independent — it neither modifies nor depends on the internal logic of any other state — each class can be generated, reviewed, and validated in isolation. Generated state classes are additive: they introduce no modifications to previously reviewed code. This corresponds directly to the incremental correctness extension property of Section 4, and structurally prevents the regression that occurs when an AI regenerates or modifies a global handler to add new states.

### 5.4 Diagram Generation as a Closed-Loop Validation Signal

The State-Scoped Command Handler pattern enables a closed-loop AI development workflow through automatic diagram generation [Nijkamp et al. 2022]: (1) describe the system in natural language; (2) AI generates state class declarations; (3) the framework generates a state diagram from the generated code; (4) the developer compares the diagram to the original specification; (5) discrepancies surface as diagram differences rather than code differences; (6) correct the prompt or code; the diagram updates immediately. This loop operates on a high-level visual artifact, making the semantic gap between specification and implementation immediately visible.

### 5.5 State-Scoped Handling as a Structural Architecture for AI Agents

Beyond AI-assisted *development*, the pattern has a direct application to the *architecture of AI agent systems*. Contemporary AI agents cycle through distinct operational modes — planning, executing, awaiting response, recovering from errors [Yao et al. 2022]. Most current agent frameworks represent this state implicitly as flags, context strings, or accumulated history. Implicit state has a well-documented failure mode [Gamma et al. 1994]: the actual state space is determined by the combinatorial product of all flags, and reasoning about valid behaviors becomes intractable as the number of flags grows.

The pattern offers an alternative: model the agent's operational modes as explicit state classes. An agent's `PlanningState` would declare handlers only for user input commands; its `ExecutingState` only for tool result commands; its `AwaitingHumanConfirmationState` only for human-response commands. Commands arriving in the wrong state are structurally excluded. This is a structural form of agent guardrailing: constraints enforced by architecture rather than by runtime assertion or prompt instruction — auditable, verifiable against the transition graph, and accurate by construction.

### 5.6 Summary

The pattern offers four properties advantageous for AI-assisted development: (1) near-bijective structural mapping from natural language specifications to code declarations; (2) rigid scaffold reducing architectural ambiguity, with compile-time validation of generated output; (3) independence of state classes enabling incremental generation and review; (4) automatic diagram generation providing a closed-loop validation signal. Additionally, the pattern's structural properties suggest a novel architecture for AI agent systems with auditable, structurally enforced behavioral constraints.

---

## 6. Reference Implementation and Case Studies

### 6.1 Framework Overview

StatePipes is an open-source .NET Core framework targeting SCADA, robotic, and IoT applications. It implements the State-Scoped Command Handler pattern in C# with routing infrastructure, automatic handler discovery, diagram generation, and a testing harness. The framework wraps the Stateless state machine library [Burrows 2009] with a strongly-typed, attribute-driven, dependency-injected API. Autofac provides the dependency injection container; Mono.Cecil provides IL-level type inspection for event detection and diagram annotation.

The framework consists of five components: the core library (`StatePipes`), a diagram generator (`StatePipes.Diagrammer`), a runtime explorer (`StatePipes.Explorer`), a scaffolding tool (`StatePipes.ServiceCreatorTool`), and a broker proxy (`StatePipes.BrokerProxy`).

### 6.2 Core Library

**State class infrastructure.** `BaseStateMachineState<TStateMachine>` scans the concrete subclass's methods at configuration time using reflection, finds methods decorated with `[PermitIf]`, `[IgnoreIf]`, or `[PermitReentryIf]`, reads the command type from the method's first parameter type, and registers a guard closure deferring execution to transition time. `ParentedBaseStateMachineState<TStateMachine, TParentState>` registers the substate relationship automatically; the generic constraint `where TParentState : BaseStateMachineState<TStateMachine>` ensures cross-machine parenting is a compile error.

**Typed command infrastructure.** `BaseTriggerCommand<TStateMachine>` is the base for all commands. The framework recommends C# `record` types for immutability and value-equality semantics.

**Framework router.** `BaseTriggerCommandHandler<TTrigger, TStateMachine>` implements `IMessageHandler<TTrigger>` and is automatically instantiated for each discovered command type. When a command arrives on the message bus, the router retrieves the machine instance from `StateMachineManager` and calls `stateMachine.Fire(command, responseInfo)`.

**Message bus.** `IStatePipesService` provides `SendCommand<T>`, `PublishEvent<T>`, and `SendResponse<T>`. The in-process implementation routes messages synchronously; `BrokerProxy` provides a networked implementation for distributed deployments.

### 6.3 The Configuration Lifecycle

**Register phase:** (1) Scans the assembly for all `BaseStateMachineState<T>` concrete types and registers them as singletons. (2) Scans for all `BaseTriggerCommand<T>` concrete types and registers a `BaseTriggerCommandHandler<TTrigger, TMachine>` for each. (3) Synthesizes a unique initialization trigger type at runtime using `System.Reflection.Emit.AssemblyBuilder`.

**Build phase:** Registers the machine with `StateMachineManager`, acquires the `TemporaryStateMachineHolder` lock, resolves and configures all state instances (performing the attribute-scanning reflection for each), releases the lock, and fires the init trigger to enter the first state. The `TemporaryStateMachineHolder` is a short-lived thread-local coupling that allows constructor-injected state classes to access their owning machine without creating a circular dependency in the container.

### 6.4 The Diagram Generation Pipeline

`StatePipes.Diagrammer` generates accurate state machine diagrams directly from a compiled service assembly through five steps: (1) load the target assembly via Mono.Cecil and inject `InternalsVisibleTo` attributes; (2) build a container with stub implementations satisfying DI registrations, without requiring hardware or network infrastructure; (3) call `StateMachineManager.SaveAllStateMachineDotGraphToPath()` to write DOT-format graphs to disk; (4) post-process with `DotGraphEnhancer`, which annotates state nodes with published events by scanning IL call sites for `PublishEvent<TEvent>()` invocations and highlights the current active state in yellow; (5) render to PDF via Graphviz `dot`. The result is always consistent with the implementation.

### 6.5 Companion Tools

**StatePipes.ServiceCreatorTool** scaffolds new services: given a machine design, it generates the machine marker, state class files, trigger command records, container setup, and optionally an entire Visual Studio solution. Generator components include tools for states, triggers, and all three guard attribute types.

**StatePipes.Explorer** is a Blazor web application providing a live view of all state machines in a running deployment: current state, highlighted diagram, available commands, published event history, and transition logs. It connects to the message bus without requiring service code modification.

**StatePipes.BrokerProxy** bridges a StatePipes service's internal message bus to an upstream network message broker (such as RabbitMQ) when the service is deployed behind a DMZ boundary. The proxy republishes commands and events between the internal bus and the upstream broker, making the StatePipes service reachable from external processes without RabbitMQ Federation or other broker-to-broker bridging. External clients use a `StatePipesProxy` client providing the same `SendCommand<T>` and `Subscribe<TEvent>` API as in-process code; the network topology is transparent to both sides.

### 6.6 Case Studies

The following case studies are drawn from production deployments of the StatePipes framework.

---

**Case Study 1: [DOMAIN — TO BE SUPPLIED BY AUTHOR]**

*System description.* [Deployed system, operating environment, constraints.]

*State machine profile.*

| Metric | Value |
|---|---|
| Total states | [N] |
| Happy-path states | [N_hp] |
| Fault and recovery states | [N_f] |
| Total command types | [\|C\|] |

*SEMC measurement.*

| State extension | New files | SEMC_scoped | Modifications made |
|---|---|---|---|
| | | | |

*Observations.* [Qualitative notes on isolation benefit, any extension rule violations, superstate usage.]

---

**Case Study 2: [DOMAIN — TO BE SUPPLIED BY AUTHOR]**

*State machine profile.*

| Metric | Value |
|---|---|
| Total states | |
| Happy-path states | |
| Fault and recovery states | |
| Total command types | |

*SEMC measurement.*

| State extension | New files | SEMC_scoped | Modifications made |
|---|---|---|---|
| | | | |

---

**Case Study 3: [DOMAIN — TO BE SUPPLIED BY AUTHOR]**

*State machine profile.*

| Metric | Value |
|---|---|
| Total states | |
| Happy-path states | |
| Fault and recovery states | |
| Total command types | |

*SEMC measurement.*

| State extension | New files | SEMC_scoped | Modifications made |
|---|---|---|---|
| | | | |

### 6.7 Cross-Case Analysis

[To be completed once case study data is supplied. Analysis should address: (1) Is SEMC_scoped consistently lower than SEMC_global? (2) How does fault state percentage vary across domains? (3) Were there extension rule violations, and how were they detected? (4) Did teams use superstates to reduce SEMC for cross-cutting transitions?]

---

## 7. Limitations and Future Work

### 7.1 Limitations of the Pattern

**L1 — The extension rule requires developer discipline.** The theorem holds under condition 3 of Definition 2. The pattern makes violations structurally visible but cannot prevent them. Safety-critical systems should combine the pattern with formal verification (FW1).

**L2 — The pattern trades command-addition cost for state-addition benefit.** Adding a new command type requires handler declarations in every responsive state. Systems with volatile command vocabularies may not benefit from the pattern's organization.

**L3 — Hierarchical state complexity introduces design coupling.** Superstates beyond two to three levels of nesting can undermine the "state class as complete behavioral specification" benefit, as substate behavior requires reading the superstate chain.

**L4 — Framework dependency constrains deployment environments.** Bare-metal embedded systems with strict memory and timing constraints may not support reflection and dynamic dispatch. The pattern's structural benefits can be partially realized through explicit registration at the cost of the zero-boilerplate property.

**L5 — Concurrent state machines require additional coordination.** The semantics of concurrent inter-machine communication and the conditions under which the incremental correctness extension property holds across machine boundaries have not been formalized.

### 7.2 Limitations of the Current Evaluation

**E1 — No controlled empirical developer study.** The practical claims about developer productivity are structural arguments, not empirical findings. A rigorous evaluation would measure task completion time, defect rate, and cognitive load [Hart and Staveland 1988] across architectures.

**E2 — SEMC is a proxy metric.** SEMC measures modification count, not defect risk. Future work should validate the relationship between SEMC and actual defect outcomes.

**E3 — The worked example is constructed, not empirical.** SEMC values in Section 4.5 are computed from design rather than measured from production change history.

**E4 — Section 5 claims are not empirically validated.** The AI-assisted development implications are analytical arguments; no controlled study has evaluated them.

**E5 — Known uses are limited to one domain.** The pattern's cost trade-offs in other domains have not been systematically evaluated.

### 7.3 Future Work

**FW1 — Formal verification integration.** The attribute-based transition declarations are machine-readable. Extracting the transition graph and feeding it to a model checker (TLA+ [Lamport 1994], Alloy [Jackson 2006], or SPIN [Holzmann 1997]) would replace developer discipline with verified enforcement of the extension rule.

**FW2 — Controlled developer study.** Construct equivalent systems in both architectures, assign state extension tasks, and measure completion time, SEMC, defect count, and self-reported cognitive load.

**FW3 — Empirical SEMC from production histories.** For each commit adding a new state to a production StatePipes system, count modified files from the version control history and compare against SEMC_global equivalents.

**FW4 — Empirical evaluation of AI code generation quality.** Prompt language models to implement state machine specifications in both architectures; compare correctness, structural consistency, and compile-time validation rates.

**FW5 — AI agent architecture instantiation.** Implement a non-trivial AI agent using the pattern; measure whether structural constraints produce improvements in reliability, auditability, or safety property compliance relative to flag-based implicit state.

**FW6 — Cross-language instantiation.** Demonstrate the pattern in a functional language (Scala, F#), a systems language (Rust), and a dynamically typed language (Python or TypeScript), revealing instantiation variants and whether any liabilities are language-specific.

**FW7 — Performance characterization for hard real-time systems.** Measure message latency and configuration time to determine suitability for soft real-time applications and identify where hard real-time constraints require a reduced-infrastructure variant.

**FW8 — Behavior tree comparison.** Formally compare the pattern against behavior trees [Marzinotto et al. 2014] along dimensions of expressiveness, composability, SEMC-equivalent cost, and fault-tolerance modeling.

**FW9 — IDE and static analysis tooling.** Develop: (a) an IDE plugin showing which states handle a given command; (b) a static analysis rule detecting extension rule violations from commit diffs; (c) live diagram preview updating as code is edited.

---

## 8. Conclusion

The State-Scoped Command Handler pattern inverts the conventional relationship between states and command handlers: instead of global handlers that check the current state, each state class declares the commands it handles as methods on that class. The framework discovers these declarations and performs routing automatically. The result is a structural organization in which a state's complete behavior is co-located, new states are additive, invalid commands are naturally excluded, and diagrams are always accurate.

The pattern's most significant practical consequence is the incremental correctness extension property, formalized in Section 4: a system satisfying its happy-path specification continues to satisfy that specification after any state extension, provided existing handled transitions are not modified. The State Extension Modification Count metric provides a measurable, version-control-computable proxy for the regression risk reduction the pattern delivers. In the worked example, four development phases produced 5 existing-file modifications under state-scoped organization versus 12 under global handler organization, with the global modifications carrying inherently higher regression exposure.

The pattern occupies a gap in the prior literature: state-scoped command dispatch has been demonstrated in standalone DSLs (P language), functional actor frameworks (Erlang gen_statem, Akka FSM), and embedded function-pointer models (Samek/QP), but not formalized as a reusable OOP framework pattern with automatic handler discovery, generic compile-time safety, and an explicit incremental development workflow.

Two directions merit particular priority in follow-on work. The empirical case for the incremental correctness extension claim — currently supported by a constructed example and structural argument — would be substantially strengthened by a controlled developer study and by SEMC measurements drawn from production system change histories, both of which are tractable with the StatePipes framework's existing deployment base. The AI agent architecture proposal of Section 5.5 — using state-scoped handling to provide structural guardrails for autonomous agent behavior — connects the pattern to one of the most active and consequential areas of current software engineering research, and warrants independent empirical investigation.

---

## Acknowledgments

[To be added.]

---

## References

- Adamczyk, P. (2003). *The Anthology of the Finite State Machine Design Patterns.* PLoP 2003.
- Aldrich, J., Sunshine, J., Saini, D., and Sparks, Z. (2009). *Typestate-Oriented Programming.* OOPSLA 2009.
- Alexander, C., Ishikawa, S., and Silverstein, M. (1977). *A Pattern Language.* Oxford University Press.
- Austin, J., et al. (2021). *Program Synthesis with Large Language Models.* arXiv:2108.07732.
- Burrows, N. (2009). *Stateless: A Hierarchical State Machine Library for .NET.* https://github.com/dotnet-state-machine/stateless.
- Chen, M., et al. (2021). *Evaluating Large Language Models Trained on Code.* arXiv:2107.03374.
- Coplien, J. and Schmidt, D., eds. (1995). *Pattern Languages of Program Design.* Addison-Wesley.
- Desai, A., Gupta, V., Jackson, E., Qadeer, S., Rajamani, S., and Zufferey, D. (2013). *P: Safe Asynchronous Event-Driven Programming.* PLDI 2013.
- Dyson, N. and Anderson, A. (1997). *State Patterns.* In Pattern Languages of Program Design 3. Addison-Wesley.
- Ericsson AB (2016). *gen_statem Behaviour.* Erlang/OTP Documentation. https://www.erlang.org/doc/system/statem.html.
- Gamma, E., Helm, R., Johnson, R., and Vlissides, J. (1994). *Design Patterns: Elements of Reusable Object-Oriented Software.* Addison-Wesley.
- Garcia, R., et al. (2014). *Foundations of Typestate-Oriented Programming.* ACM TOPLAS 36(4).
- Harel, D. (1987). *Statecharts: A Visual Formalism for Complex Systems.* Science of Computer Programming 8(3), 231–274.
- Hart, S. G. and Staveland, L. E. (1988). *Development of NASA-TLX (Task Load Index): Results of Empirical and Theoretical Research.* Advances in Psychology 52, 139–183.
- Hindle, A., Barr, E. T., Su, Z., Gabel, M., and Devanbu, P. (2012). *On the Naturalness of Software.* ICSE 2012.
- Holzmann, G. J. (1997). *The Model Checker SPIN.* IEEE Transactions on Software Engineering 23(5), 279–295.
- Jackson, D. (2006). *Software Abstractions: Logic, Language, and Analysis.* MIT Press.
- Khourshid, D. (2016). *XState: State Machines and Statecharts for the Modern Web.* https://github.com/statelyai/xstate.
- Lamport, L. (1994). *The Temporal Logic of Actions.* ACM TOPLAS 16(3), 872–923.
- Lightbend. *Akka Classic FSM.* https://doc.akka.io/libraries/akka-core/current/fsm.html.
- Marzinotto, A., Colledanchise, M., Smith, C., and Ögren, P. (2014). *Towards a Unified Behavior Trees Framework for Robot Control.* ICRA 2014.
- Meyer, B. (1988). *Object-Oriented Software Construction.* Prentice Hall.
- Mukherjee, S., Deligiannis, P., Lal, A., and Lichtenberg, A. (2019). *Reliable State Machines: A Framework for Programming Reliable Cloud Services.* ECOOP 2019.
- Nijkamp, E., et al. (2022). *CodeGen: An Open Large Language Model for Code with Multi-Turn Program Synthesis.* arXiv:2203.13474.
- OMG (2003). *Unified Modeling Language Specification, Version 2.0.* Object Management Group.
- Samek, M. (2008). *Practical UML Statecharts in C/C++: Event-Driven Programming for Embedded Systems.* 2nd ed. Newnes.
- Selic, B., Gullekson, G., and Ward, P. T. (1994). *Real-Time Object-Oriented Modeling.* Wiley.
- Strom, R. E. and Yemini, S. (1986). *Typestate: A Programming Language Concept for Enhancing Software Reliability.* IEEE Transactions on Software Engineering 12(1).
- W3C (2015). *State Chart XML (SCXML): State Machine Notation for Control Abstraction.* W3C Recommendation.
- Yao, S., et al. (2022). *ReAct: Synergizing Reasoning and Acting in Language Models.* arXiv:2210.03629.
- Ziegler, A., et al. (2022). *Productivity Assessment of Neural Code Completion.* MAPS 2022.
