# StatePipes Developer Guide

How to set up a Windows machine, write StatePipes microservices, and run and deploy them in Docker.

**Contents**

1. [Setup](#1-setup)
2. [Programming Model](#2-programming-model)
3. [Visual Studio External Tools](#3-visual-studio-external-tools)
4. [StatePipes Claude Code Plugin](#4-statepipes-claude-code-plugin)
5. [Run Scripts](#5-run-scripts)
6. [Docker Container Tools](#6-docker-container-tools)

Deeper references:

- [StateMachine API (composition guide)](https://github.com/marlinsr/StatePipes/wiki/4.0-StateMachine-API): every type, attribute and helper with examples.
- [Visual Studio External Tools User Guide](https://github.com/marlinsr/StatePipes/wiki/StatePipes-Visual-Studio-External-Tools-User-Guide): what each tool generates, and headless mode.
- [Paper](https://github.com/marlinsr/StatePipes/blob/main/docs/Paper.md): the state-scoped command handler pattern and why StatePipes works this way.
- [Training videos](http://www.youtube.com/@StatePipes).

---

## 1. Setup

### System requirements

- Windows 11.

### Software requirements

Install these in order.

#### Docker Desktop

Install [Docker Desktop](https://www.docker.com/products/docker-desktop/) with the WSL 2 backend and leave it running. Every
infrastructure container (step-ca, RabbitMQ, Kafka, PostgreSQL, StatePipes.Explorer) and every containerized service runs
in it.

#### Visual Studio

Install [Visual Studio](https://visualstudio.microsoft.com/) with the **ASP.NET and web development** workload. Install it
before the StatePipes installer: the installer adds the StatePipes External Tools to every Visual Studio it finds.

#### Hosts file entries

Services and your browser reach the infrastructure containers by their container names. TLS hostname verification
depends on this, so these names have to resolve on the host too. Open Notepad **as Administrator**, open
`C:\Windows\System32\drivers\etc\hosts`, and add:

```
127.0.0.1 statepipes.explorer
127.0.0.1 step-ca
127.0.0.1 amqp09-broker
127.0.0.1 kafka-broker
127.0.0.1 postgres-db
```

The **New StatePipes Sln** tool checks these names and reminds you of any that are missing.

#### OpenSSL

In an Administrator command prompt:

```
winget install openssl
```

Then add `C:\Program Files\OpenSSL-Win64\bin` to your `PATH` (**Settings → System → About → Advanced system settings →
Environment Variables**). Open a new terminal afterwards so it picks up the new path.

#### Claude Code

Install [Claude Code](https://docs.anthropic.com/en/docs/claude-code/overview) and run `claude` once to sign in. Install it before
the StatePipes installer so the installer can register the StatePipes plugin (see
[section 4](#4-statepipes-claude-code-plugin)).

#### StatePipes ServiceCreatorTool installer

Download the latest `StatePipes.ServiceCreatorTool.Installer.exe` from
[Releases](https://github.com/marlinsr/StatePipes/releases) and run it. It installs to `C:\Program Files (x86)\StatePipes`
and:

- Imports the StatePipes External Tools into each installed (non-preview) Visual Studio. It opens Visual Studio briefly
  to do this; that window closes by itself.
- Creates the `StatePipesLocalNugets` user environment variable (`%USERPROFILE%\.statepipes\PrivateNugets`) and adds it
  to your NuGet sources as **StatePipes Private Nugets**, for testing locally built StatePipes packages.
- Registers and installs the StatePipes Claude Code plugin if it finds `claude`.

Run the installer again whenever you want to upgrade. The tools and the plugin then match the new version.

---

## 2. Programming Model

### Domain-Driven Design for a microservice

A StatePipes microservice is one bounded context: it owns its domain model and exposes it only through messages. The
model has two parts:

- **The domain.** Entities and value objects: the things the service manages and the rules about them.
- **The behavior.** One or more state machines. Each state is a class, and each state declares the triggers (commands)
  it handles. The framework routes every trigger to the current state, so you never write `switch (state)` code.

This is the **state-scoped command handler** pattern. Adding a state means adding a class, plus a handler in each state
that leads to it. Existing states don't change.

**New StatePipes Service** generates three projects:

```
<Prefix>/                         class library: all behavior lives here
  Builders/                       DefaultSetup.cs (DI container), DefaultServiceConfiguration.cs (broker + proxies)
  Entities/                       domain entities (create the folder with the first entity)
  Events/                         public events this service publishes
  ValueObjects/                   domain value objects, plus the generated ProxyMonikers.cs
  Proxies/                        generated proxies to other services (never hand-edited)
  StateMachines/<Machine>/
    <Machine>.cs                  the state machine marker interface
    States/                       one class per state
    Triggers/                     public triggers
    Triggers/Internal/            internal triggers
<Prefix>.Service/                 host: Program.cs, appsettings, Dockerfile. No behavior.
<Prefix>.Test/                    StateMachineForTest tests
```

`<Prefix>` is `<Company>.<Product>`, for example `Acme.Widgets`. Keep all behavior in the class library. The
`.Service` project only hosts it.

### Events

An event is a fact that has already happened, named in the past tense (`PumpStartedEvent`). It implements `IEvent` and
must be immutable, because every subscriber receives the same object. Use a `record` or get-only properties.

```csharp
namespace Acme.Widgets.Events
{
    public record PumpStartedEvent(int Rpm) : IEvent;
}
```

A state broadcasts an event with `PublishEvent(...)`, or replies to the sender of the current trigger with
`SendResponse(ev, responseInfo)`.

**Public and internal events.** The class's visibility decides where the event goes:

| | Declared | Where it goes |
|---|---|---|
| **Public event** | `public` class in `Events/` | To local subscribers **and** to the broker, on `<service>.events`. Other services, generated proxies and StatePipes.Explorer see it. It is part of the service's API. |
| **Internal event** | `internal` class | Local subscribers and in-process handlers only. It is never sent to the broker. |

Generated proxies use internal events. To send a command to another service, a state publishes the proxy's
`<Command>To<Moniker>Event`, and the proxy forwards it (see [Proxies](#proxies)).

### State machines

A state machine is a marker interface in `StateMachines/<Machine>/<Machine>.cs`:

```csharp
public interface PumpStateMachine : IStateMachine { }
```

A service can have several state machines. Each one has its own current state, and each trigger belongs to exactly one
machine. A state can fire a trigger into another machine of the same service with
`FireExternal<OtherMachine, Trigger>(...)`.

The machines run on [Stateless](https://github.com/dotnet-state-machine/stateless). StatePipes builds the Stateless
configuration from your state classes, so you never configure Stateless directly.

### States

A state is a class named after a condition (`Idle`, `Running`, `Faulted`) in `States/`:

```csharp
internal class Idle : BaseStateMachineState<PumpStateMachine>, IFirstStateForStateMachine
{
    public override void OnEntry() { }
    public override void OnExit() { }
    public override void OnActivate() { }
    public override void OnDeActivate() { }

    [PermitIf(typeof(Running))]
    private bool StartGuard(Start trigger, BusConfig? responseInfo) => trigger.Rpm > 0;
}
```

- **Lifecycle.** Put every side effect of entering or leaving the state in `OnEntry` or `OnExit`: starting or stopping
  timers, publishing "entered" events, opening or closing resources. `OnActivate` and `OnDeActivate` run when the
  machine itself is activated or deactivated.
- **First state.** The machine's first state implements `IFirstStateForStateMachine`.
- **Hierarchy.** A child state derives from `ParentedBaseStateMachineState<TMachine, TParent>`. A trigger that the child
  doesn't handle falls through to its parent. The parent's first child implements `IFirstSubstate` and is entered
  automatically with the parent. Use a parent when several states share handling for a trigger, for example a `Stop` that
  is valid in every running sub-mode.
- **Timers.** `CreateDelayedMessageSender<T>()` returns a timer that sends a trigger later. Use it for timeouts and
  periodic work (the **Add Periodic** tool wires one up for you).

### Triggers

A trigger is a command to one state machine, named after the request (`Start`, `Stop`, `ResetFault`). It derives from
`BaseTriggerCommand<TMachine>` and carries its payload as immutable properties.

| | Declared | Who can send it |
|---|---|---|
| **Public trigger** | `public class` in `Triggers/` | Anyone: other services through a generated proxy, StatePipes.Explorer, and this service. It is part of the service's API, so change it deliberately. |
| **Internal trigger** | `internal class` in `Triggers/Internal/` | Only this service: timers, proxy connection status, cross-machine firing, and the triggers generated proxies create from other services' events. The broker can't route to it. |

```csharp
public class Start(int rpm) : BaseTriggerCommand<PumpStateMachine>
{
    public int Rpm { get; } = rpm;
}
```

Trigger and event payloads carry value objects or plain values, never entities, because they cross the service
boundary.

### Guards

A state handles a trigger with a guard method: a private method that takes the trigger and the sender's `BusConfig`,
returns `bool`, and has one of three attributes. Name it `<Trigger>Guard`.

| Attribute | Returns `true` | Returns `false` |
|---|---|---|
| `[PermitIf(typeof(Dest))]` | Transition to `Dest` (`OnExit`, then `Dest.OnEntry`) | No transition from this state |
| `[PermitReentryIf]` | Leave and re-enter this state (`OnExit`, then `OnEntry`) | No transition, but **the method body has already run**. Use this to handle a command in place: publish an event, send a response, update state. |
| `[IgnoreIf]` | The trigger is dropped on purpose | Not ignored |

If every guard for a trigger in the current state returns `false`, the trigger falls through to the parent state's
guards. If no ancestor handles it, it is dropped and logged at verbose level. A state that has no guard for a trigger
doesn't react to it, and that is often the right design.

Keep guards fast and non-blocking: never use `.Result` or `.Wait()`. When the decision is a domain rule, ask the
entity, for example `pump.CanStart(trigger.Rpm)`. Don't put the rule in the guard.

### Workflows

A workflow is a state machine with **many persisted instances**, one per `CorrelationId`, stored in PostgreSQL. Use one
for long-running processes that have many concurrent copies, such as orders, batches or approval requests. A singleton
state machine is the right choice for a device or a subsystem.

```csharp
public interface OrderWorkflow : IWorkflow { }
```

Everything above applies, with these differences:

- States derive from `BaseWorkflowState<TWorkflow>` or `ParentedBaseWorkflowState<TWorkflow, TParent>`. Triggers derive
  from `BaseWorkflowTriggerCommand<TWorkflow>`, and their `CorrelationId` selects the instance. The first command for an
  unknown `CorrelationId` creates the instance, if the first state handles that command.
- Every instance field of every workflow state is persisted with the instance, so keep fields JSON-serializable.
- **Workflow states have a parameterless constructor only: no dependency injection.** To reach a singleton part of the
  service, `SendCommand` to a singleton state machine and include the `CorrelationId`. The singleton replies with a
  workflow trigger that carries the same `CorrelationId`.
- Set `CorrelationId = CorrelationId` on every message a workflow sends to itself, timer messages included.
- At least one state implements `IFinalWorkflowState`. Entering it deletes the instance and its timers.
- **Add Workflow** adds the PostgreSQL `WorkflowStoreConfig` to `DefaultServiceConfiguration` for you. Test workflows
  with `WorkflowForTest<TWorkflow>`, which uses an in-memory store and clock.

### Entities

An entity has an identity that lasts while its attributes change: a `Pump`, a `Batch`, a `Recipe`. A value object is
defined only by its attributes: `Rpm`, `PumpId`, `Setpoint`.

| Kind | Folder and namespace | Shape |
|---|---|---|
| Entity | `<Prefix>/Entities/`, `<Prefix>.Entities` | A class with an identity property; equality by identity. It owns its domain rules. |
| Value object | `<Prefix>/ValueObjects/`, `<Prefix>.ValueObjects` | An immutable `record`; equality by value; validated in its constructor. |

Put one type per file, named after the type. Don't put domain types in `StateMachines/`, `Builders/`, `Events/` or a
`Models/` folder.

### Dependency injection for entities and states

Each service has an [Autofac](https://autofac.org/) container. The framework registers every state of every state
machine as a **single instance** and resolves it through its constructor. So a state gets its entities, and any other
dependency, by asking for them in its constructor:

```csharp
internal class Running : BaseStateMachineState<PumpStateMachine>
{
    private readonly Pump _pump;
    public Running(Pump pump) { _pump = pump; }

    [PermitIf(typeof(Idle))]
    private bool StopGuard(Stop trigger, BusConfig? responseInfo) => _pump.CanStop();
}
```

You register entities and other dependencies yourself, in `Builders/DefaultSetup.cs`. This is the class that
`ServiceConfiguration` names as the container setup:

```csharp
public class DefaultSetup : IContainerSetup
{
    public void Register(ContainerBuilder containerBuilder)
    {
        containerBuilder.RegisterType<Pump>().AsSelf().SingleInstance();
        // generated proxy registrations are injected here too
    }

    public void Build(IContainer container)
    {
        // post-build wiring; generated proxies are started here
    }
}
```

- Register an entity as `SingleInstance` when several states share it, which is usual: they all see the same `Pump`.
- Keep the domain rules on the entity, and keep state classes thin.
- Don't edit the `// Injection Point` lines. The tools insert proxy registrations there.
- In tests, `StateMachineForTest.Register<T>(mock.Object)` replaces a dependency. Anything you don't register is filled
  with a dummy automatically (see the generated `DummyDependencyRegistration`).
- **Workflows are the exception.** Workflow states are created by the framework for each instance and persisted, so they
  can't take constructor dependencies (see [Workflows](#workflows)).

### ServiceConfiguration

`ServiceConfiguration` is everything a service needs to start. `Builders/DefaultServiceConfiguration.cs` builds the
default, and `Program.cs` hands it to the host:

```csharp
return new ServiceConfiguration(
    new BusConfig("amqps://amqp09-broker/Production",          // broker URI (ssl://... selects Kafka)
                  "Acme.Widgets",                              // exchange (service) name
                  "amqpuser.amqp09-broker.client.p12",        // client certificate
                  "CommonPassword.txt"),                       // file holding the certificate password
    setup.Assembly.GetName().Name!,                            // class library that holds the behavior
    setup.FullName!,                                           // its IContainerSetup (DefaultSetup)
    proxies,                                                   // ProxyConfigurations: other services this one talks to
    substitutions,                                             // ProxySubstitutions: share a parent's proxy
    new ServiceArgs(null),                                     // default command-line style args
    workflowStore);                                            // WorkflowStoreConfig, only when the service has workflows
```

| Part | Purpose |
|---|---|
| `BusConfig` | Which broker, which exchange name, which client certificate. Commands arrive on `<exchange>.commands` and events leave on `<exchange>.events`. An `ssl://` URI (for example `ssl://kafka-broker:9093`) selects Kafka; anything else (for example `amqps://amqp09-broker/Production`) selects RabbitMQ. |
| `ProxyConfigurations` | One entry per proxied service: a name (the moniker), a type, and that service's own `ServiceConfiguration`. `RemoteService` connects over the broker. `LocalService` and `LocalServiceWithRemoteAccess` host the other service in the same process, the latter still reachable through the broker. The proxy tools add these entries. |
| `ProxySubstitutions` | Lets a locally hosted child service use a proxy its parent already has, instead of opening its own. |
| `ServiceArgs` | `--name=value` arguments such as `--loglevel`, `--postfix` and `--companyname`. Arguments given on the command line are merged on top of these. |
| `WorkflowStore` | The PostgreSQL connection for workflows: host `postgres-db`, database and role `statepipes`, and a client certificate. |

**Where the configuration actually comes from.** On every start the service writes the configuration it is using to
`%ProgramData%\<Company>\<Prefix>.Service\Config\ServiceConfiguration.json` (inside a Linux container,
`/usr/share/<Company>/...`). With `--usedefaultconfig=true`, which is what the run scripts pass, it uses the compiled
default. Without that argument it reads the JSON file, so you can change broker, certificates or proxies on a deployed
service without rebuilding it. Certificates are looked up in the `Certs` folder next to it.

**Running several copies.** `--postfix=2` appends `2` to the exchange name, so the copy runs as `Acme.Widgets2`.
`--postfixrecursiveaddtoproxies=true` applies the same postfix to every proxied service too, for a fully separate set.

### Proxies

A proxy lets a state machine talk to another StatePipes service with strongly typed messages. It is generated, never
hand-written:

- **New StatePipes Proxy** generates it from the other service's compiled DLL.
- **Create StatePipes Proxy From Service** generates it from a running service's self-description, through the broker.
  This works for services you don't have the source for, including StatePipes.OpcUaBridge.
- **Update StatePipes Proxy** and **Update StatePipes Proxy From Service** regenerate it after the other service changes.

Each tool writes `Proxies/<Moniker>Proxy.cs` and, the first time, injects the proxy's registration into `DefaultSetup`,
its `ProxyConfiguration` into `DefaultServiceConfiguration`, and a moniker constant into `ValueObjects/ProxyMonikers.cs`.

The generated file turns the other service's API into this service's own internal messages, so states use a proxy the
same way they use everything else:

| Other service has | Proxy generates | How a state uses it |
|---|---|---|
| Public trigger `StartSampling` | Internal event `StartSamplingTo<Moniker>Event` | `PublishEvent(new StartSamplingToSensorEvent(proxyName, data))` sends the command. An empty `proxyName` sends it to every instance of that proxy. |
| Public event `SampleTakenEvent` | Internal trigger `SampleTakenEventFrom<Moniker>Trigger` | Handle it with a guard, like any other trigger. |
| Connects or disconnects | `ProxyConnectionStatusTrigger(proxyName, isConnected)` | Handle it in the first state machine to react to the other service going away. |

The proxied service's payload types are copied into the generated file as value objects, so your service has no
compile-time reference to the other service.

---

## 3. Visual Studio External Tools

The installer adds these tools to the Visual Studio **Tools** menu (from `StatePipesExternalToolsSettings.vssettings`).
Run them with the right project or file active. Each tool saves all open documents first and writes to the **Output**
window. Every tool also has a headless command-line form, for scripts, CI and Claude Code.

| Tool | Run with | What it does |
|---|---|---|
| **New StatePipes Sln** | Nothing open | Asks for RabbitMQ or Kafka, a repo folder and a name like `Packages.Acme.Widgets`. Creates the solution, the `RunScript` and `BuildScript` folders, `CLAUDE.md`, and the first service, then opens it in Visual Studio. |
| **New StatePipes Service** | The solution | Adds another service (class library, `.Service` host and `.Test` project) and its run-script entries. |
| **Add StateMachine** | The class library | Adds a state machine: its marker interface and its first state, `TopLevelState`. |
| **Add Workflow** | The class library | Adds a workflow: a persisted, multi-instance state machine with a first state and a final state. It also wires up the PostgreSQL workflow store. |
| **Add State** | The class library | Adds a state to a machine you pick, optionally as a child of an existing state. |
| **Add Trigger** | The class library | Adds a public or internal trigger to a machine. |
| **Add Event** | The class library | Adds a public event in `Events/`. |
| **Add PermitIf** | A state file | Adds a `[PermitIf]` guard to that state for a trigger and destination you pick. |
| **Add PermitReentryIf** | A state file | Adds a `[PermitReentryIf]` guard: handle the trigger in place, or re-enter the state. |
| **Add IgnoreIf** | A state file | Adds an `[IgnoreIf]` guard. |
| **Add Periodic** | A state file | Adds a timer that starts in `OnEntry`, stops in `OnExit`, and fires an internal trigger at an interval while the machine is in that state. |
| **New StatePipes Proxy** | The class library | Generates a proxy from another service's compiled DLL and wires it into the service. |
| **Update StatePipes Proxy** | A `*Proxy.cs` file | Regenerates that proxy from the updated DLL. |
| **Create StatePipes Proxy From Service** | The class library | Generates a proxy from a running service's self-description over the broker. |
| **Update StatePipes Proxy From Service** | A `*Proxy.cs` file | Regenerates that proxy from the running service. |
| **StateMachine Diagrammer** | The class library | Builds a PDF diagram of every state machine from the compiled assembly, including the events each state publishes. |

A typical sequence for a new service: **New StatePipes Sln** → **Add StateMachine** (or use the generated one) →
**Add State** for each state → **Add Trigger** and **Add Event** → **Add PermitIf / PermitReentryIf / IgnoreIf** in each
state → build → **StateMachine Diagrammer** to check the result.

See the [Visual Studio External Tools User Guide](https://github.com/marlinsr/StatePipes/wiki/StatePipes-Visual-Studio-External-Tools-User-Guide)
for exactly what each tool creates and injects, and for headless mode.

---

## 4. StatePipes Claude Code Plugin

The `statepipes` plugin teaches Claude Code to design, scaffold, extend and verify StatePipes services the same way the
External Tools and this guide do. It is a set of skills:

| Skill | Use it when |
|---|---|
| `statepipes-domain-fit` | Starting a new service or feature: gives a go or no-go on whether StatePipes fits. The rule of thumb: if the service behaves differently depending on its state or mode, StatePipes fits. |
| `statepipes-domain-modeling` | After a go: turns your domain description into a written model (entities, value objects, machines, states, triggers, events, guards, timers, proxies) that you approve before any code is written. |
| `statepipes-scaffold` | Creating or extending code. Each operation mirrors an External Tool, and Claude runs the tool headless where it can. |
| `statepipes-conventions` | Reading or editing any StatePipes service: layout, naming, lifecycle, guards and framework rules. |
| `statepipes-verify` | Before calling a change done: `dotnet format`, build, tests (one `StateMachineForTest` test per new transition), a check for blocking calls, and a comparison with the approved model. |

**New service:** `domain-fit` → `domain-modeling` → `scaffold` + `conventions` → `verify`.
**Extending a service:** `conventions` + `scaffold` → `verify`.

Claude picks the skills itself. Describe what you want ("add a Faulted state that the pump enters when the sensor
disconnects") from the solution folder. Each new solution gets a `CLAUDE.md` that lists its services and points Claude at
the plugin.

### Installing

The installer registers the plugin automatically when it finds `claude`. To install it yourself, in a `claude` session:

```
/plugin marketplace add marlinsr/StatePipes
/plugin install statepipes@statepipes
```

If the installer ran under a different Windows account (for example, you approved it with an administrator's
credentials), run these two commands yourself. See the
[plugin README](https://github.com/marlinsr/StatePipes/tree/main/StatePipes.ClaudePlugin) for updating, uninstalling and
loading a local copy.

---

## 5. Run Scripts

**New StatePipes Sln** (or the plugin's `new-solution` operation) creates two script folders next to the solution.
**New StatePipes Service** adds each new service to them. Run the scripts from PowerShell.

```
Packages.Acme.Widgets/
  RunScript/
    DockerInfrastructureStart.ps1
    DockerInfrastructureStop.ps1
    Start.ps1
    Stop.ps1
    Acme.Widgets.Service.DockerStart.ps1      one pair per service
    Acme.Widgets.Service.DockerStop.ps1
  BuildScript/
    NugetConfig.xml
    Acme.Widgets.Service.LocalImageBuild.ps1  one per service
```

### `DockerInfrastructureStart.ps1`: start the shared infrastructure

Run it **as Administrator**, once when you start working and again whenever you want a clean environment. It tears down
any previous infrastructure and rebuilds it on a Docker network named `<Company>-net`:

1. **step-ca** (`smallstep/step-ca`): a private certificate authority. It issues every TLS certificate below from one
   root, and the script copies them to `C:\ProgramData\SmallStep\step-ca\`.
2. **amqp09-broker** (RabbitMQ 4): TLS only, on port 5671, with client-certificate login and the vhosts `Production` and
   `VirtualHardware`. The management UI is at `https://amqp09-broker:15671`.
3. **kafka-broker** (Apache Kafka, KRaft): mutual TLS on port 9093, with topic auto-creation off.
4. **postgres-db** (PostgreSQL 17): the workflow store. It accepts only TLS logins with a client certificate (role
   `statepipes`). Its data lives in the Docker volume `postgres-db-data`, which the script keeps, so workflow instances
   survive a restart. Run `docker volume rm postgres-db-data` to start with an empty database.
5. **statepipes.explorer**: the web UI, at `https://statepipes.explorer:7219`.

It then installs the root, intermediate and server certificates into the Windows machine certificate store, so your
browser trusts the Explorer and the RabbitMQ management UI. Both brokers always start; the solution uses whichever one
you picked when you created it.

### `DockerInfrastructureStop.ps1`: stop it

Stops and removes the five containers and the network. It keeps the PostgreSQL volume.

### `Start.ps1`: run your services locally

Runs your **locally built** services (`<Svc>.Service\bin\Debug\net11.0\<Svc>.Service.exe`) against the Docker
infrastructure, and opens a StatePipes.Explorer tab for each one. Build the solution in Debug first.

For each service, `Start.ps1` copies the broker client certificate and `CommonPassword.txt` from step-ca's folder into
the service's `Certs` folder. It then starts the service with `--usedefaultconfig=true` and opens Chrome at
`https://statepipes.explorer:7219/Params/<broker>/<service>/<cert>/CommonPassword.txt/<service>`, which connects the
Explorer straight to that service.

Each service you add appends a line like this:

```powershell
StartService "Acme.Widgets" "" $false
```

The script defines three functions you can use to change the lines it generates:

| Function | Use |
|---|---|
| `StartService $name $postfix $recursive` | Start the service and open its Explorer tab. |
| `StartServiceNoChromeTab $name $postfix $recursive` | Start the service without a browser tab. |
| `StartChromeTab $name $postfix` | Open an Explorer tab only. |

A non-empty `$postfix` starts another copy of the same service under the exchange name `<name><postfix>`, for example
`StartService "Acme.Widgets" "2" $false`. Set `$recursive` to `$true` to give its proxied services the same postfix.

### `Stop.ps1`: stop them

Stops every service process that `Start.ps1` started.

### `<Svc>.Service.DockerStart.ps1` / `DockerStop.ps1`: run a service as a container

`DockerStart` runs the service's Docker image (`<svc>-service:latest`) on the infrastructure network, with the CA
certificates mounted and the client certificates copied into `%ProgramData%\<Company>\<Svc>.Service\Certs`. It also
copies the PostgreSQL client certificate when there is one. It then opens the Explorer on the service. `DockerStop`
stops and removes the container. Use these to check that the service works as it will be deployed. Build the image
first, from Visual Studio's Docker support or with the script below.

### `BuildScript/<Svc>.Service.LocalImageBuild.ps1`: build an image with local packages

You need this only when you are testing **locally built StatePipes NuGet packages** inside a container. It builds the
service's Docker image with the packages from `%StatePipesLocalNugets%` (restored through `NugetConfig.xml`) and their
PDBs, so you can debug into them. For normal work, build the image from Visual Studio.

### Typical loop

```powershell
# once (as Administrator)
.\RunScript\DockerInfrastructureStart.ps1
# edit, build in Visual Studio, then
.\RunScript\Start.ps1
# ...use the Explorer tabs...
.\RunScript\Stop.ps1
```

You can also run or debug a single service straight from Visual Studio while the infrastructure is up.

---

## 6. Docker Container Tools

All three tools are published to Docker Hub with each release, as `:latest` and `:v<version>`. Each one reads its
certificates from `/usr/share/StatePipes/<Tool>/Certs` and trusts the broker by mounting `root_ca.pem` and
`intermediate_ca.pem` into `/etc/ssl/certs`.

### StatePipes.Explorer: `bigfish88/statepipesexplorer`

A web UI for any running StatePipes service. `DockerInfrastructureStart.ps1` starts it at
`https://statepipes.explorer:7219`.

- **Connect** by entering a broker URI, a service (exchange) name, a client certificate and its password file. You can
  also open a `/Params/...` URL, which is what the run scripts do.
- **Home** lists every public command and event of the service, from the service's self-description. Fill in a command
  and send it. Watch events arrive, with live charts for numeric values. A show filter and an object-editor filter
  narrow long lists, and **URL With Filters** gives you a link that reopens the same view.
- **Log File** shows the tail of the service's log. The built-in `SetLogLevelCommand` and `GetLogLevelCommand`, listed
  with the other commands, change and read the log level.
- **State Machine** and **Workflow** show the live diagrams, with the current state highlighted.
- A communication-status panel shows whether the Explorer is connected to the broker and to the service.

### StatePipes.BrokerProxy: `bigfish88/statepipesbrokerproxy`

Makes a service on one broker appear as a service on another broker. Use it, for example, when a service sits behind a
DMZ boundary, or when a system must orchestrate a service on another system's broker. No broker federation is needed.

The BrokerProxy connects to the real service on the **source** broker as a client, and serves it on the **proxy** broker
under a name you choose. Commands sent to the proxy name go to the real service. The real service's events are
republished on the proxy broker. Responses find their way back across the hop. Clients on the proxy side use an
ordinary `StatePipesProxy` or generated proxy and can't tell the difference.

| Variable (or `--NAME=value`) | Meaning |
|---|---|
| `SOURCEBROKER`, `SOURCEEXCHANGE` | Broker URI and service name of the real service |
| `SOURCECERTPATH`, `SOURCEPWPATH` | Client certificate (`.p12`) and password file for the source broker |
| `PROXYBROKER`, `PROXYEXCHANGE` | Broker URI and service name to appear as |
| `PROXYCERTPATH`, `PROXYPWPATH` | Client certificate and password file for the proxy broker |

Either side can be RabbitMQ or Kafka.

```sh
docker run -d --name widgets-brokerproxy --network Acme-net \
  -v C:/ProgramData/StatePipes/StatePipes.BrokerProxy/Certs:/usr/share/StatePipes/StatePipes.BrokerProxy/Certs:ro \
  -v C:/ProgramData/SmallStep/step-ca/root_ca.pem:/etc/ssl/certs/root_ca.pem:ro \
  -v C:/ProgramData/SmallStep/step-ca/intermediate_ca.pem:/etc/ssl/certs/intermediate_ca.pem:ro \
  -e SOURCEBROKER=amqps://amqp09-broker/VirtualHardware -e SOURCEEXCHANGE=Acme.Widgets \
  -e SOURCECERTPATH=amqpuser.amqp09-broker.client.p12 -e SOURCEPWPATH=CommonPassword.txt \
  -e PROXYBROKER=amqps://amqp09-broker/Production -e PROXYEXCHANGE=Acme.Widgets \
  -e PROXYCERTPATH=amqpuser.amqp09-broker.client.p12 -e PROXYPWPATH=CommonPassword.txt \
  bigfish88/statepipesbrokerproxy:latest
```

### StatePipes.OpcUaBridge: `bigfish88/statepipesopcuabridge`

Presents an OPC UA server, such as a PLC, as a StatePipes service. On startup it browses the server and generates
messages at runtime:

- For each readable variable: a **Get** command (read once), a **Monitor** command (subscribe to changes), a **Delete**
  command, and the matching **Get** and **MonitorStatus** events.
- For each writable variable: a **Set** command and a **SetStatus** event.
- For each callable method: an **Exec** command and an **ExecResult** event.

It answers self-description requests and sends heartbeats, so the Explorer can drive it. **Create StatePipes Proxy From
Service** can also generate a typed proxy to the PLC's address space, which your state machines then use like any other
proxy.

Main settings: `BROKER`, `EXCHANGE`, `CERTPATH`, `PWPATH` (the StatePipes side), `OPCUA_ENDPOINT`, `OPCUA_USE_SECURITY`,
`OPCUA_USERNAME` / `OPCUA_PASSWORD`, and the regex filters `OPCUA_NODE_FILTER`, `OPCUA_WRITE_FILTER` and
`OPCUA_METHOD_FILTER`. Monitors are saved to a mounted `Config` volume and survive restarts of both the PLC and the bridge.

```sh
docker run -d --name opcuabridge --network Acme-net \
  -v /path/to/certs:/usr/share/StatePipes/StatePipes.OpcUaBridge/Certs:ro \
  -v /path/to/root_ca.pem:/etc/ssl/certs/root_ca.pem:ro \
  -v /path/to/intermediate_ca.pem:/etc/ssl/certs/intermediate_ca.pem:ro \
  -v opcuabridge-pki:/pki \
  -v opcuabridge-config:/usr/share/StatePipes/StatePipes.OpcUaBridge/Config \
  -e OPCUA_ENDPOINT=opc.tcp://plc:4840 \
  -e BROKER=amqps://amqp09-broker/Production -e EXCHANGE=Line1Plc \
  -e CERTPATH=amqpuser.amqp09-broker.client.p12 -e PWPATH=CommonPassword.txt \
  bigfish88/statepipesopcuabridge:latest
```

Message naming, the Set and Exec type rules, monitor behavior, ordering guarantees and size limits for large servers are
covered in the
[OpcUaBridge README](https://github.com/marlinsr/StatePipes/blob/main/StatePipes.OpcUaBridge/README.md).
