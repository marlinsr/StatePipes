# add-trigger

External Tool: **Add Trigger** (`-t`). Generator: `TriggerGeneratorTool.GenerateTriggerFile`.

## Inputs

- Service class library `<Prefix>` (not the `.Service` or `.Test` project).
- State machine `<Machine>` (an existing folder under `<Prefix>/StateMachines/`).
- Scope: **public** (other services may send it through a proxy) or **internal** (only this service sends it,
  for example timers, proxy status, cross-machine signals). See `statepipes-conventions` → Triggers.
- Trigger name `<Trigger>`, PascalCase, no `Trigger` suffix required (timer triggers from `add-periodic` get one).

## Steps

1. Public: create the file from

   Template: Trigger_cs.sample
   Creates: <Prefix>/StateMachines/<Machine>/Triggers/<Trigger>.cs

   Internal: create the file from

   Template: InternalTrigger_cs.sample
   Creates: <Prefix>/StateMachines/<Machine>/Triggers/Internal/<Trigger>.cs

2. Substitute `@#$ClassLibraryName@#$` = `<Prefix>`, `@#$StateMachineName@#$` = `<Machine>`, `@#$TriggerName@#$` =
   `<Trigger>`.
3. Add any payload as get-only properties set by a constructor, matching the style of the service's existing
   triggers.

## Next

A trigger does nothing until a state handles it: follow with `add-permit-if`, `add-permit-reentry-if` or
`add-ignore-if` in each state that must react, then `statepipes-verify`.
