# add-permit-reentry-if

External Tool: **Add PermitReentryIf** (`-pri <state file>`). Generator:
`PermitReentryIfGaurdGeneratorTool.GeneratePermitReentryIfGuard`.

## Inputs

- The state file `<Prefix>/StateMachines/<Machine>/States/<State>.cs`.
- A trigger `<Trigger>` of the same machine.

## Steps

1. Take the snippet from

   Template: PermitReentryIfGuard_cs.sample

   substitute `@#$TriggerName@#$` = `<Trigger>`.
2. Insert it on the lines immediately **above** this marker, keeping the marker:

   Marker: // Injection Point 5, Do Not Delete or Modify

3. Enable the trigger's using line as in `add-permit-if` step 3.
4. Decide the return value:
   - `true`: re-enter the state (runs `OnExit` then `OnEntry`).
   - `false`: run the method body as a side effect only, without cycling the lifecycle. Use this to handle a
     command in place (update data, publish an event).

   See `docs/StateMachineComposition.md` section 7.3.
