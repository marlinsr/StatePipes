# StatePipes Visual Studio External Tools

These tools are registered in Visual Studio by importing `StatePipesExternalToolsSettings.vssettings` (found in `StatePipes.ServiceCreatorToolSetup\Resources\`). They appear in the Visual Studio **Tools** menu and accelerate scaffolding and diagramming workflows without leaving the IDE.

All tools pipe their output to the Visual Studio **Output** window (`UseOutputWindow = true`) and save all open documents before running (`SaveAllDocs = true`).

---

## Visual Studio Macro Reference

The argument strings use standard Visual Studio External Tools macros:

| Macro | Expands to |
|-------|-----------|
| `$(SolutionDir)` | Full path to the directory containing the open solution |
| `$(SolutionFileName)` | File name of the open solution (with `.sln` extension) |
| `$(ProjectFileName)` | File name of the active project (with `.csproj` extension) |
| `$(TargetDir)` | Full output directory of the active project (e.g. `bin\Debug\net8.0\`) |
| `$(BinDir)` | Bin directory of the active project (e.g. `bin\`) |
| `$(ItemPath)` | Full path of the file currently selected or open in the editor |

---

## Tool Summary

| Menu Title | Index | Executable | Requires Open File |
|------------|-------|------------|-------------------|
| New StatePipes Sln | 300 | ServiceCreatorTool | No |
| New StatePipes Service | 301 | ServiceCreatorTool | No |
| New StatePipes Proxy | 302 | ServiceCreatorTool | No |
| StateMachine Diagrammer | 303 | Diagrammer | No |
| Add StateMachine | 304 | ServiceCreatorTool | No |
| Add Trigger | 305 | ServiceCreatorTool | No |
| Add State | 306 | ServiceCreatorTool | No |
| Add PermitIf | 307 | ServiceCreatorTool | Yes — state class file |
| Add PermitReentryIf | 308 | ServiceCreatorTool | Yes — state class file |
| Add IgnoreIf | 309 | ServiceCreatorTool | Yes — state class file |
| Update StatePipes Proxy | 310 | ServiceCreatorTool | Yes — `*Proxy.cs` file |
| Add Periodic | 311 | ServiceCreatorTool | Yes — state class file |

---

## Detailed Tool Descriptions

---

### New StatePipes Sln

**Menu title:** New StatePipes Sln
**Executable:** `%ProgramFiles(x86)%\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`
**Arguments:** *(none)*
**When to use:** Starting a brand-new StatePipes solution from scratch, outside of any existing Visual Studio session.

#### What happens

1. Opens a **folder browser dialog** — select the repo root directory where the new solution folder will be created.
2. Opens an **input dialog** — enter the solution name in the format `Packages.MyCompany.MyProduct` (the `Packages.` prefix is stripped from the solution name but kept as the directory name).
3. The tool creates the full solution scaffold:

| Path | Contents |
|------|----------|
| `<SolutionDir>\` | `.sln`, `.gitignore`, `.dockerignore`, `SolutionInfo.proj`, `SetupAndRunInstructions.pdf` |
| `<SolutionDir>\RunScript\` | `DockerInfrastructureStart.ps1`, `DockerInfrastructureStop.ps1`, `Start.ps1`, `Stop.ps1` |
| `<SolutionDir>\BuildScript\` | `NugetConfig.xml` |

4. If `statepipes.explorer`, `step-ca`, or `amqp09-broker` are not resolvable in the local hosts file, a reminder message is shown listing the required `/etc/hosts` entries.
5. Launches a new Visual Studio instance with the generated solution.

---

### New StatePipes Service

**Menu title:** New StatePipes Service
**Executable:** `%ProgramFiles(x86)%\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`
**Arguments:** `-r $(SolutionDir) -s $(SolutionFileName)`
**When to use:** Adding a new StatePipes service (class library + service host + test project) to an existing solution.

#### What happens

1. Builds the solution (`dotnet build`) to ensure it is current.
2. Opens an **input dialog** — enter a project prefix in the format `{SolutionName}.{ServiceName}` (e.g., `MyProduct.Vhw`).
3. Creates three projects and injects them into the `.sln` file:

**Class library** (`<Prefix>\`)

| Path | File |
|------|------|
| `<Prefix>\` | `<Prefix>.csproj`, `Docs\ReadMe.md` |
| `<Prefix>\Builders\` | `DefaultSetup.cs`, `DefaultServiceConfiguration.cs` |
| `<Prefix>\Events\` | `DummyEvent.cs`, `CurrentStatusEvent.cs` |
| `<Prefix>\ValueObjects\` | `ProxyMonikers.cs` |
| `<Prefix>\StateMachines\<StateMachine>\` | `<StateMachine>.cs` |
| `<Prefix>\StateMachines\<StateMachine>\States\` | `ParentState.cs` |
| `<Prefix>\StateMachines\<StateMachine>\Triggers\` | `DummyTrigger.cs`, `SendCurrentStatusCommand.cs` |
| `<Prefix>\StateMachines\<StateMachine>\Triggers\Internal\` | `ProxyConnectionStatusTrigger.cs` |

**Service host** (`<Prefix>.Service\`)

| Path | File |
|------|------|
| `<Prefix>.Service\` | `<Prefix>.Service.csproj`, `Program.cs`, `Dockerfile`, `appsettings.json`, `appsettings.Development.json` |
| `<Prefix>.Service\Properties\` | `launchSettings.json` |

**Test project** (`<Prefix>.Test\`)

| Path | File |
|------|------|
| `<Prefix>.Test\` | `<Prefix>.Test.csproj`, `DummyDependencyRegistration.cs`, `StateMachineDummyTests.cs`, `TestCategories.cs` |

**RunScript and BuildScript additions**

| Path | File |
|------|------|
| `RunScript\` | `<Prefix>.Service.DockerStart.ps1`, `<Prefix>.Service.DockerStop.ps1` (entries also added to `Start.ps1` / `Stop.ps1`) |
| `BuildScript\` | `<Prefix>.Service.LocalImageBuild.ps1` |
| Solution root | `Tests.runsettings` |

---

### New StatePipes Proxy

**Menu title:** New StatePipes Proxy
**Executable:** `%ProgramFiles(x86)%\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`
**Arguments:** `-r $(SolutionDir) -s $(SolutionFileName) -p $(ProjectFileName) -b $(TargetDir)`
**When to use:** Adding a client proxy for a remote StatePipes service to the currently active service project. Run this with a **service class library project** (not the `.Service` or `.Test` project) active in Solution Explorer.

#### What happens

1. Builds the solution.
2. Opens an **input dialog** — enter a moniker for the proxy (a short identifier, e.g., `Sensor`).
3. Opens a **DLL browser** pointing at `$(TargetDir)` — select the compiled assembly of the service you want to connect to.
4. Generates `<Moniker>Proxy.cs` in `<ClassLibrary>\Proxies\`, containing strongly-typed send-command and subscribe-event members derived from the selected assembly's public triggers and events.
5. If this is a new proxy (file did not already exist), injects wiring into:
   - `Builders\DefaultSetup.cs` — registration call
   - `Builders\DefaultServiceConfiguration.cs` — proxy configuration entry
   - `ValueObjects\ProxyMonikers.cs` — moniker constant

---

### StateMachine Diagrammer

**Menu title:** StateMachine Diagrammer
**Executable:** `%ProgramFiles(x86)%\StatePipes\Diagrammer\StatePipes.Diagrammer.exe`
**Arguments:** `-r $(SolutionDir) -s $(SolutionFileName) -p $(ProjectFileName) -c $(BinDir)`
**When to use:** Generating up-to-date PDF state machine diagrams for all state machines in the active project's service assembly. Run with a **service class library project** active.

#### What happens

1. Builds the solution.
2. Resolves the service assembly path. If the active project is not a `.Service` project, the tool automatically redirects to the corresponding `.Service\bin\<configuration>\` directory.
3. Loads the service assembly and discovers all registered state machines.
4. For each state machine, generates a Graphviz dot graph (enhanced with event annotations) and renders it to a PDF.
5. Saves PDF files to:
   ```
   %ProgramData%\StatePipes\StatePipes.Diagrammer\
   ```
6. Output is printed to the Visual Studio Output window, including the path of each generated file.

---

### Add StateMachine

**Menu title:** Add StateMachine
**Executable:** `%ProgramFiles(x86)%\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`
**Arguments:** `-r $(SolutionDir) -s $(SolutionFileName) -p $(ProjectFileName) -b $(TargetDir) -m`
**When to use:** Adding a new state machine to an existing service project. Run with a **service class library project** active.

#### What happens

1. Builds the solution.
2. Checks that the active project is a service class library (not a `.Service` or `.Test` project).
3. Opens an **input dialog** — enter the name for the new state machine (e.g., `Pump`).
4. Creates:

| Path | File | Contents |
|------|------|----------|
| `<ClassLibrary>\StateMachines\<Name>\` | `<Name>.cs` | `IStateMachine` marker class |
| `<ClassLibrary>\StateMachines\<Name>\States\` | `TopLevelState.cs` | First state class implementing `IFirstStateForStateMachine` |

---

### Add Trigger

**Menu title:** Add Trigger
**Executable:** `%ProgramFiles(x86)%\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`
**Arguments:** `-r $(SolutionDir) -s $(SolutionFileName) -p $(ProjectFileName) -b $(TargetDir) -t`
**When to use:** Adding a new trigger to an existing state machine. Run with a **service class library project** active. The project must be built so the tool can reflect on the available state machines.

#### What happens

1. Builds the solution.
2. Reflects on the project's compiled assembly to discover available state machines and presents a **selection list** — choose the target state machine.
3. Presents a **selection list** — choose `Public` or `Internal` scope.
   - *Public* triggers appear in the `Triggers\` folder and are accessible to external proxy clients.
   - *Internal* triggers appear in `Triggers\Internal\` and are only used within the service.
4. Opens an **input dialog** — enter the trigger name.
5. Creates the trigger record class file from the appropriate template at:
   - `<ClassLibrary>\StateMachines\<Machine>\Triggers\<Name>.cs` (Public)
   - `<ClassLibrary>\StateMachines\<Machine>\Triggers\Internal\<Name>.cs` (Internal)

---

### Add State

**Menu title:** Add State
**Executable:** `%ProgramFiles(x86)%\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`
**Arguments:** `-r $(SolutionDir) -s $(SolutionFileName) -p $(ProjectFileName) -b $(TargetDir) -a`
**When to use:** Adding a new state to an existing state machine. Run with a **service class library project** active. The project must be built.

#### What happens

1. Builds the solution.
2. Reflects on the compiled assembly and presents a **selection list** — choose the target state machine.
3. Presents a **selection list** — choose `Un-parented` or `Parented`.
   - **Un-parented** — creates a flat state inheriting `BaseStateMachineState<TStateMachine>`.
   - **Parented** — presents a second selection list of existing states; the new state will inherit `ParentedBaseStateMachineState<TStateMachine, TParentState>`. If no other state currently uses the chosen parent, the new state is generated as the `IFirstSubstate` (first child) automatically.
4. Opens an **input dialog** — enter the state name.
5. Creates the state class file at `<ClassLibrary>\StateMachines\<Machine>\States\<Name>.cs` using one of three templates:
   - `UnparentedState` — flat state
   - `FirstParentedState` — first child of a parent (includes `IFirstSubstate`)
   - `ParentedState` — additional child of an already-parented parent

---

### Add PermitIf

**Menu title:** Add PermitIf
**Executable:** `%ProgramFiles(x86)%\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`
**Arguments:** `-r $(SolutionDir) -s $(SolutionFileName) -p $(ProjectFileName) -b $(TargetDir) -pi $(ItemPath)`
**When to use:** Adding a `[PermitIf]` guarded transition to an existing state class. **Open or select the target state's `.cs` file in the editor before running.**

#### What happens

1. Builds the solution.
2. Determines the state machine from the open state file by inspecting the source.
3. Reflects on the compiled assembly and presents two **selection lists** in sequence:
   - Select the trigger that will fire the transition.
   - Select the destination state.
4. Injects a `[PermitIf(typeof(<DestinationState>))]` guard method stub into the open state file at the designated injection point.
5. Removes the internal or public trigger `using` comment placeholder from the file based on whether the selected trigger is internal or public.

The injected stub returns `true` unconditionally. Replace `return true;` with real guard logic as needed.

---

### Add PermitReentryIf

**Menu title:** Add PermitReentryIf
**Executable:** `%ProgramFiles(x86)%\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`
**Arguments:** `-r $(SolutionDir) -s $(SolutionFileName) -p $(ProjectFileName) -b $(TargetDir) -pri $(ItemPath)`
**When to use:** Adding a `[PermitReentryIf]` handler to an existing state class. **Open or select the target state's `.cs` file in the editor before running.**

#### What happens

1. Builds the solution.
2. Determines the state machine from the open state file.
3. Reflects on the compiled assembly and presents a **selection list** — choose the trigger.
4. Injects a `[PermitReentryIf]` guard method stub into the open state file.
5. Removes the internal or public trigger `using` comment placeholder.

The injected stub returns `true` (re-enters the state, cycling `OnExit`/`OnEntry`). Change the return value to `false` to execute the method body as a side effect without cycling the state's lifecycle — see the [Guard Attributes](StateMachineComposition.md#73-permitreentryif) section of the composition guide for details.

---

### Add IgnoreIf

**Menu title:** Add IgnoreIf
**Executable:** `%ProgramFiles(x86)%\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`
**Arguments:** `-r $(SolutionDir) -s $(SolutionFileName) -p $(ProjectFileName) -b $(TargetDir) -ii $(ItemPath)`
**When to use:** Adding a `[IgnoreIf]` handler to suppress a trigger in an existing state class. **Open or select the target state's `.cs` file in the editor before running.**

#### What happens

1. Builds the solution.
2. Determines the state machine from the open state file.
3. Reflects on the compiled assembly and presents a **selection list** — choose the trigger to suppress.
4. Injects an `[IgnoreIf]` guard method stub into the open state file.
5. Removes the internal or public trigger `using` comment placeholder.

The injected stub returns `true` (the trigger is always silently dropped in this state). Replace with real guard logic to conditionally suppress the trigger.

---

### Update StatePipes Proxy

**Menu title:** Update StatePipes Proxy
**Executable:** `%ProgramFiles(x86)%\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`
**Arguments:** `-r $(SolutionDir) -s $(SolutionFileName) -p $(ProjectFileName) -b $(TargetDir) -u $(ItemPath)`
**When to use:** Regenerating an existing proxy class after the remote service's public API has changed. **Open or select the existing `<Moniker>Proxy.cs` file in Solution Explorer before running.**

#### What happens

1. Builds the solution.
2. Extracts the proxy moniker from the selected file name (everything before `Proxy.cs`).
3. Opens a **DLL browser** pointing at `$(TargetDir)` — select the updated compiled assembly of the remote service.
4. Overwrites `<Moniker>Proxy.cs` with a freshly generated proxy class reflecting the current triggers and events in the selected assembly.
5. Because the proxy file already exists, the `DefaultSetup.cs`, `DefaultServiceConfiguration.cs`, and `ProxyMonikers.cs` injection step is **skipped** — the existing wiring is preserved.

---

### Add Periodic

**Menu title:** Add Periodic
**Executable:** `%ProgramFiles(x86)%\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`
**Arguments:** `-r $(SolutionDir) -s $(SolutionFileName) -p $(ProjectFileName) -b $(TargetDir) -pti $(ItemPath)`
**When to use:** Adding a recurring timer-driven trigger to an existing state. **Open or select the target state's `.cs` file in the editor before running.**

#### What happens

1. Builds the solution.
2. Determines the state machine from the open state file.
3. Opens an **input dialog** — enter a timer name (e.g., `Poll`). This becomes `PollTrigger` and `PollPeriodicWorker`.
4. Opens an **input dialog** — enter the period in milliseconds.
5. Creates an **internal trigger** class `<Name>Trigger.cs` in `Triggers\Internal\`.
6. Injects the following boilerplate into the open state file at the designated injection points:

| Injection | What is added |
|-----------|---------------|
| Field | A `<Name>PeriodicWorker` field that drives the timer |
| Constructor | Initialization of the periodic worker with the specified period |
| `OnEntry` | Code to start the periodic worker when the state is entered |
| `OnExit` | Code to stop the periodic worker when the state is exited |
| Guard method | A `[PermitReentryIf]` method that fires on each timer tick |

The net result is a self-contained polling loop: the timer starts on `OnEntry`, fires `<Name>Trigger` at the configured interval (which re-enters the state via `[PermitReentryIf]`, running the guard method body each tick), and stops automatically on `OnExit`.

---

## Typical Workflow

```
New StatePipes Sln          ← once per repository
  └─ New StatePipes Service ← once per microservice
       ├─ Add StateMachine  ← once per state machine
       │    ├─ Add State    ← for each additional state
       │    ├─ Add Trigger  ← for each transition signal
       │    ├─ Add PermitIf        ┐
       │    ├─ Add PermitReentryIf ├─ per guard in a state file
       │    ├─ Add IgnoreIf        ┘
       │    └─ Add Periodic        ← per timed behaviour in a state
       ├─ New StatePipes Proxy    ← for each remote service dependency
       ├─ Update StatePipes Proxy ← when a dependency's API changes
       └─ StateMachine Diagrammer ← at any time to visualise
```
