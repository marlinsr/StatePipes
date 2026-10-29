---
name: statepipes-domain-modeling
description: Use after statepipes-domain-fit returns Go - turns a domain description into a written StatePipes model (domain entities and value objects, machines, states, triggers, events, guards, timers, proxies) that the user approves before any scaffolding or code.
---

# Model the service before writing code

Requirements for stateful systems usually read as *"when in state S and command C arrives, if condition X, go to
T or do Y"*. That maps directly onto StatePipes: S is a state class, C is a trigger, X is a guard body, and T is a
`[PermitIf(typeof(T))]` (`docs/Paper.md` 5.1). Write the model in that shape. The scaffold operations then follow
from it mechanically.

StatePipes services use **Domain-Driven Design**. Name the domain's entities (things with an identity that lasts
while their attributes change) and value objects (immutable values defined only by their attributes) in the model,
and use the domain's own terms for them and for states, triggers and events.

## Process

1. Start from the domain-fit verdict's candidate states and state-dependent commands.
2. **Happy path first.** Model the primary operating path as a complete, working machine. List fault, recovery and
   maintenance states as a later phase. They are added without changing the happy-path transitions
   (`docs/Paper.md` section 4).
3. Ask the user one question at a time wherever the description leaves a transition or condition open. Don't
   invent behavior.
4. Write the model with the headings below, then ask the user to approve it. **Don't scaffold anything until the
   user approves.**

## Model template

```markdown
# <Service> StatePipes model

Service: <Company>.<Product>.<Component>   (class library <Prefix>)
Phase: happy path | phase 2: <name> | ...

## Domain model
| Type | Kind | Identity / attributes | Used by |
|---|---|---|---|
| Pump | entity | PumpId; Rpm, Pressure | Running, StatusEvent |
| Rpm | value object | int > 0 | Start payload |

## Machines
- <Machine>StateMachine — <what it controls>. (One machine per independently-changing lifecycle; most services need one.)

## States (per machine)
| State | Parent | First? | Meaning | OnEntry / OnExit work |
|---|---|---|---|---|
| Idle | — | first state | ... | publish IdleEvent |

## Triggers
| Trigger | Scope | Payload | Sent by |
|---|---|---|---|
| Start | public | TargetRpm:int | operator UI via proxy |
| PollTrigger | internal | — | periodic timer in Running |

## Transitions and handlers (per state)
| State | Trigger | Kind | Condition | Result |
|---|---|---|---|---|
| Idle | Start | PermitIf → Running | TargetRpm > 0 | — |
| Running | Start | IgnoreIf | always | already running |
| Running | PollTrigger | PermitReentryIf (false) | always | read sensors, publish StatusEvent |

## Events
| Event | Payload | Published by (state, when) |
|---|---|---|

## Periodic timers
| State | Timer | Period ms | Per-tick work |
|---|---|---|---|

## Proxies (other services this service uses)
| Moniker | Remote service | Triggers sent | Events consumed |
|---|---|---|---|

## Open questions
- ...
```

Rules for filling it in:

- Every trigger must appear in at least one state's handler row. If a trigger has no row in some state, it is
  dropped there. Write that down explicitly only if the user cares, as an `IgnoreIf` row.
- `PermitReentryIf` must say `(true)` (re-enter) or `(false)` (handle in place). See `statepipes-conventions` →
  Guards.
- Classify every domain type as an entity or a value object. Trigger and event payloads use value objects, or
  plain values, never mutable entities. An entity's identity is a value object, for example `PumpId`.
- Use parent states for handling that several states share, instead of copying the same row into each state.
- Proxies list *other* services as given inputs. This skill models one service and doesn't design the others or
  where the boundaries between services fall.

## From model to operations

Once the model is approved, apply `statepipes-scaffold` operations in this order:

| Model element | Operation |
|---|---|
| New service | `new-service` (or `new-solution` for a new repository) |
| Each additional machine | `add-state-machine` |
| Each state | `add-state` (parents before their children) |
| Each trigger | `add-trigger` (timer triggers come from `add-periodic` instead) |
| Each `PermitIf` / `PermitReentryIf` / `IgnoreIf` row | `add-permit-if` / `add-permit-reentry-if` / `add-ignore-if` on the row's state |
| Each periodic timer | `add-periodic` on its state |
| Each entity | `Entities/<Entity>.cs`, creating `<Prefix>/Entities/` if it doesn't exist (see `statepipes-conventions` → Domain-Driven Design) |
| Each value object | `ValueObjects/<ValueObject>.cs` |
| Each event | `Events/<Event>.cs` from `Event_cs.sample` (see the scaffold skill) |
| Each proxy | `new-proxy` |

Then fill in the guard conditions and `OnEntry`/`OnExit` work following `statepipes-conventions`, and finish with
`statepipes-verify`. Keep the approved model in the service repository as `<Prefix>/StatePipesModel.md` so the
`diagram` output can be compared against it.
