---
name: statepipes-scaffold
description: Use when creating or extending a StatePipes service - solution, service, state machine, state, trigger, guard (PermitIf, PermitReentryIf, IgnoreIf), periodic timer, proxy, or diagram. Gives the exact files, templates and injection points the StatePipes Visual Studio External Tools would produce.
---

# StatePipes scaffold operations

Every structural change to a StatePipes service is one of twelve operations. Each mirrors a Visual Studio External
Tool from `StatePipes.ServiceCreatorTool` and has a recipe in `operations/`. **Follow the recipe exactly**: same
folders, file names, templates and injection markers as the tool, so the result is indistinguishable from what a
developer gets in Visual Studio. (A headless ServiceCreatorTool will replace these recipes with CLI calls; the
operation names will not change.)

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

**Events** have no External Tool menu entry. Create `<Prefix>/Events/<Event>.cs` from `Event_cs.sample`
(`@#$EventName@#$` = `<Event>`), as `EventGeneratorTool` does, and publish it from a state with `PublishEvent`.

## Where the templates are

Templates are `.sample` files in `StatePipes.ServiceCreatorTool/Resources/` of the StatePipes repository. A
service repository does not contain them. Read them from the StatePipes release the service uses, so they match its
package version (`StatePipes` `PackageReference` in `<Prefix>.csproj`, for example `6.0.3`):

```
https://raw.githubusercontent.com/marlinsr/StatePipes/v<version>/StatePipes.ServiceCreatorTool/Resources/<Template>
```

If you are working inside the StatePipes repository itself, read them from disk.

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
| `@#$SolutionName@#$` | Solution name without `Packages.` and `.slnx`, e.g. `Acme.Widgets` |
| `@#$CompanyName@#$` / `@#$ProductName@#$` | First / remaining part of the solution name (`Acme` / `Widgets`) |
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
