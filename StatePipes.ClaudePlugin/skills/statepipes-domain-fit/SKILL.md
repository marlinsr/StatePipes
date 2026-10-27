---
name: statepipes-domain-fit
description: Use when starting a new microservice or a major new feature and deciding whether to build it with StatePipes - gives a go or no-go verdict with reasons before any modeling or code.
---

# Does this service fit StatePipes?

**Rule: if a microservice under development has distinct behavior based on state or mode, StatePipes should be
used.**

StatePipes (the state-scoped command handler pattern, `docs/Paper.md` section 3) helps when the same command
must be handled differently depending on what the service is doing. If behavior never depends on a state or mode,
the pattern only adds framework overhead.

## Checklist

Answer each question from the user's description. Quote the words that support each answer.

1. **State-dependent commands.** Is a command valid, ignored or handled differently depending on the current state
   or mode? For example, `Start` is accepted when idle and ignored when running.
2. **Named states or modes.** Does the description name conditions the service is in, such as idle, running,
   faulted, maintenance, connected or calibrating? Three or more states is typical (`docs/Paper.md` 3.3).
3. **Lifecycle work.** Does something have to happen on entering or leaving a condition? Examples are starting a
   timer, publishing a status change, or opening a connection.
4. **Time-driven behavior.** Is there polling, a timeout or a periodic task that only runs in some states?
5. **Growth by new states.** Are fault handling, recovery or new modes expected to be added later?
   (`docs/Paper.md` section 4: adding states is additive.)

## Verdict

- **Go:** question 1 is yes, or question 2 is yes and at least one of questions 3 to 5 is yes. Name the candidate
  states and the state-dependent commands you found, then continue with `statepipes-domain-modeling`.
- **No-go:** all answers are no. Typical cases are stateless request/response services, CRUD over a store, and pure
  data transformation pipelines. Say why, suggest a plain .NET service instead, and stop. Don't scaffold
  StatePipes code.
- **Borderline:** the service holds data but has a single operating mode, or its "modes" are only configuration
  flags. Ask the user one question: *"Does the service behave differently depending on a mode or condition
  it's in (for example, refusing or deferring some commands)?"* Then decide from their answer. Don't decide a
  borderline case on your own.

Report the verdict in this form:

```
Verdict: Go | No-go | Borderline (asking)
Evidence: <quoted phrases mapped to checklist questions>
Candidate states: <list, or none>
State-dependent commands: <command → behavior per state, or none>
```

## Worked examples

- **Pump controller** ("idle until started, runs at a set speed, faults on over-pressure, can be reset only when
  stopped"). Go. States: Idle, Running, Faulted. `Start` moves Idle to Running and is ignored while Running. `Reset`
  is valid only in Faulted. An over-pressure reading moves Running to Faulted. Status polling happens only while
  Running. See also `docs/Paper.md` 4.5.
- **Inventory API** ("create, read, update and delete items; list by category"). No-go. Every request is handled
  the same way regardless of any condition, so this is CRUD.

## Scope

This skill covers one service at a time. It does not decide how to split a system into several services.
