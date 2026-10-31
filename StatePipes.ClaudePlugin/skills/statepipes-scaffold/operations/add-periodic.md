# add-periodic

External Tool: **Add Periodic** (`-pti <state file>`). Generator: `PeriodicTriggerGeneratorTool.GeneratePeriodicTrigger`.

A timer that runs only while the state is active: it starts in `OnEntry`, fires an internal trigger every period,
the state handles each tick with a side-effect-only `[PermitReentryIf]`, and it stops in `OnExit`.

## Inputs

- The state file `<Prefix>/StateMachines/<Machine>/States/<State>.cs`.
- Timer name `<Timer>` (for example `Poll`), and period in milliseconds `<Ms>`.

Derived names (the tool capitalises the first letter of the timer name, skipping leading non-letters):
`<Name>` = capitalised `<Timer>`; trigger `<Name>Trigger`; guard method `<Name>PeriodicWorker`.

## Run the tool

Build first (`dotnet build <SolutionDir>\<Solution>.slnx`), then:

```
& "<Tool>" -headless <common arguments> -pti "<state file>" --timer-name <Timer> --timer-period <Ms>
```

The tool creates the trigger, makes every insertion and enables the using line. Then write the per-tick work as step 4 describes.

## Fallback (tool without headless support)

1. Create the internal trigger exactly as `add-trigger` (internal) does, named `<Name>Trigger`:

   Template: InternalTrigger_cs.sample
   Creates: <Prefix>/StateMachines/<Machine>/Triggers/Internal/<Name>Trigger.cs

2. Insert each snippet immediately **above** its marker in the state file, keeping each marker. Substitute
   `@#$TimerName@#$` = `<Timer>` (as typed), `@#$TriggerName@#$` = `<Name>Trigger`, `@#$TimerPeriod@#$` = `<Ms>`,
   `@#$PeriodicGuard@#$` = `<Name>PeriodicWorker`.

   | Snippet | Marker |
   |---|---|
   | `PeriodicField_cs.sample` | Injection Point 6 (field) |
   | `PeriodicConstructor_cs.sample` | Injection Point 7 (constructor) |
   | `PeriodicOnEntry_cs.sample` | Injection Point 8 (`OnEntry`) |
   | `PeriodicOnExit_cs.sample` | Injection Point 9 (`OnExit`) |
   | `PeriodicGuard_cs.sample` | Injection Point 5 (guards) |

   Template: PeriodicField_cs.sample
   Template: PeriodicConstructor_cs.sample
   Template: PeriodicOnEntry_cs.sample
   Template: PeriodicOnExit_cs.sample
   Template: PeriodicGuard_cs.sample
   Marker: // Injection Point 5, Do Not Delete or Modify
   Marker: // Injection Point 6, Do Not Delete or Modify
   Marker: // Injection Point 7, Do Not Delete or Modify
   Marker: // Injection Point 8, Do Not Delete or Modify
   Marker: // Injection Point 9, Do Not Delete or Modify

3. Delete `//@#$InternalTriggers@#$` from the start of the internal-triggers using line.
4. Put the per-tick work in the `<Name>PeriodicWorker` body. It **must keep returning `false`**: `true` would
   re-enter the state and restart the timer every tick.
