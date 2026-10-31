---
name: statepipes-scaffold
description: Use when creating or extending a StatePipes service - solution, service, state machine, state, trigger, guard (PermitIf, PermitReentryIf, IgnoreIf), periodic timer, proxy, or diagram. Gives the exact files, templates and injection points the StatePipes Visual Studio External Tools would produce.
---

# StatePipes scaffold operations

Every structural change to a StatePipes service is one of twelve operations. Each mirrors a Visual Studio External
Tool from `StatePipes.ServiceCreatorTool` and has a recipe in `operations/`. **Follow the recipe exactly**: same
folders, file names, templates and injection markers as the tool, so the result is indistinguishable from what a
developer gets in Visual Studio. `new-proxy` and `update-proxy` are the exception: Claude runs the installed tool
headless for them (see "Running the tool headless"), because their output is generated from another service.

Read `statepipes-conventions` before writing code inside the generated files.

## Operations

| Operation | External Tool menu title | Recipe |
|---|---|---|
| `new-solution` | New StatePipes Sln | `operations/new-solution.md` |
| `new-service` | New StatePipes Service | `operations/new-service.md` |
| `add-state-machine` | Add StateMachine | `operations/add-state-machine.md` |
| `add-state` | Add State | `operations/add-state.md` |
| `add-trigger` | Add Trigger | `operations/add-trigger.md` |
| `add-permit-if` | Add PermitIf | `operations/add-permit-if.md` |
| `add-permit-reentry-if` | Add PermitReentryIf | `operations/add-permit-reentry-if.md` |
| `add-ignore-if` | Add IgnoreIf | `operations/add-ignore-if.md` |
| `add-periodic` | Add Periodic | `operations/add-periodic.md` |
| `new-proxy` | New StatePipes Proxy | `operations/new-proxy.md` |
| `update-proxy` | Update StatePipes Proxy | `operations/update-proxy.md` |
| `diagram` | StateMachine Diagrammer | `operations/diagram.md` |

Usual order (the External Tools' typical workflow): `new-solution` once per repository → `new-service` once per
microservice → per machine `add-state-machine`, then `add-state` / `add-trigger` / guards / `add-periodic` →
`new-proxy` per remote dependency → `diagram` any time.

The installer registers three more External Tools, which are folded into the operations above:

- **Add Event** (`-e`): creates `<Prefix>/Events/<Event>.cs` from `Event_cs.sample` (`@#$EventName@#$` = `<Event>`).
  Do this by hand exactly as `EventGeneratorTool` does, then publish the event from a state with `PublishEvent`.
- **Create StatePipes Proxy From Service** and **Update StatePipes Proxy From Service** (`-live`): the same as
  `new-proxy` and `update-proxy`, but the proxy is built from the description a running service publishes on the
  broker, so the other service's DLL isn't needed. See `operations/new-proxy.md`.

## Running the tool headless

`<Tool>` is the installed ServiceCreatorTool, normally
`${env:ProgramFiles(x86)}\StatePipes\ServiceCreatorTool\StatePipes.ServiceCreatorTool.exe`. Check that it exists.
`-headless` skips Visual Studio (it doesn't attach, build or open files), and every input a dialog would ask for is
given as an argument. A missing input makes the tool print `Headless mode needs ...` rather than open a dialog.

The common arguments name the solution and the class library, the same values Visual Studio passes:

```
-r "<SolutionDir>\" -s <Solution>.slnx -p <Prefix>.csproj -b "<SolutionDir>\<Prefix>\obj\Debug\net10.0\"
```

`-b` must be the class library's **`obj`** output folder for the configuration you built. The tool derives
`<Prefix>.Service/bin/<configuration>/` from it. Run in PowerShell. Success ends with `Finished!!!`. Anything else
(`Aborting Exception: ...`, `Incorrect Parameters Specified!`, `Headless mode needs ...`) is a failure: report the
message, and don't write the generated files by hand.

## The StatePipes version

Templates and documents must come from the same StatePipes release that the service references.

- **Existing service:** use the `StatePipes` `PackageReference` version in `<Prefix>.csproj`, for example `6.0.3`.
- **New solution or first service (no csproj yet):** use the latest release tag of
  `https://github.com/marlinsr/StatePipes` (`v<version>`, for example `v6.0.3`). Use that same `<version>` for
  `@#$StatePipesVersion@#$`, so the new csproj references the package that matches the templates. Tell the user
  which version you picked.

## Where the templates and documents are

Templates are `.sample` files in `StatePipes.ServiceCreatorTool/Resources/` of the StatePipes repository, and
the reference documents are in `docs/`. A service repository has neither, so read them from the release:

```
https://raw.githubusercontent.com/marlinsr/StatePipes/v<version>/StatePipes.ServiceCreatorTool/Resources/<Template>
https://raw.githubusercontent.com/marlinsr/StatePipes/v<version>/docs/<Document>.md
```

Every skill that cites `docs/StateMachineComposition.md`, `docs/Paper.md` or `docs/SETUP.md` means the document
at that URL. If you are working inside the StatePipes repository itself, read them from disk.

## Recipe format

Recipes are prose plus machine lines that `StatePipes.Test/ClaudePlugin` checks against the tool:

- `Template: <file>.sample` — a template the operation uses.
- `Marker: <text>` — an injection marker the operation inserts above.
- `Creates: <path>` — a file the operation creates, relative to the solution directory.

**Injecting** means: substitute the snippet's placeholders, then insert it on the lines immediately above the
marker line, and keep the marker line unchanged so later operations can use it again. Never delete or edit a
`// Injection Point N, Do Not Delete or Modify` line.

## Placeholders

Templates use `@#$Name@#$` placeholders. Substitute every one; a leftover `@#$` in a generated file is a bug,
except the `//@#$PublicTriggers@#$` and `//@#$InternalTriggers@#$` prefixes on state using lines, which stay until
a guard operation removes them.

| Placeholder | Value |
|---|---|
| `@#$SolutionName@#$` | Solution file name without `.slnx`, `Packages.` prefix kept if present, e.g. `Packages.Acme.Widgets` |
| `@#$CompanyName@#$` / `@#$ProductName@#$` | With `Packages.` removed: first / remaining part of the solution name (`Acme` / `Widgets`) |
| `@#$ClassLibraryName@#$` | `<Prefix>`, e.g. `Acme.Widgets.Pump` |
| `@#$StateMachineName@#$` | `<Machine>`; default for a new service `<Component>StateMachine` |
| `@#$StateName@#$`, `@#$ParentStateName@#$`, `@#$TriggerName@#$`, `@#$TransitionToStateName@#$`, `@#$EventName@#$` | As named in the recipe |
| `@#$ContainerImageName@#$` | `<Prefix>` with `.` → `-`, lower-case, plus `-service` |
| `@#$BuildFunctionName@#$` | `<Prefix>` with `.` → `_`, lower-case, plus `_Service_LocalImageBuild` |
| `@#$NetworkName@#$` | `<CompanyName>-net` |
| `@#$EventExchange@#$` / `@#$InboundExchange@#$` | `<Prefix>.Service.V1.Event` / `<Prefix>.Service.V1.Inbound` |
| `@#$BrokerUri@#$` | RabbitMQ `amqps://amqp09-broker/Production`, Kafka `ssl://kafka-broker:9093` |
| `@#$BrokerUriForStartScript@#$` | `@#$BrokerUri@#$` with `/` → `%2F` |
| `@#$CertFileName@#$` | RabbitMQ `amqpuser.amqp09-broker.client.p12`, Kafka `kafkauser.kafka-broker.client.p12` |
| `@#$StatePipesVersion@#$` | The StatePipes package version |
| `@#$Year@#$` | Current year |
| `@#$ProxyInjection1@#$`, `2`, `3`, `4` | `// Injection Point 1, Do Not Delete or Modify` (and 2, 3, 4) |
| `@#$ContainerInjection1@#$` | `# Injection Point 1, Do Not Delete or Modify` |
| Project and secrets GUIDs | New GUIDs (project GUIDs braced, secrets GUID plain) |

## After every operation

Build (`dotnet build`). After the last operation of a change, run `statepipes-verify`.
