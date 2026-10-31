# add-permit-if

External Tool: **Add PermitIf** (`-pi <state file>`). Generator: `PermitIfGaurdGeneratorTool.GeneratePermitIfGuard`.

## Inputs

- The source state file `<Prefix>/StateMachines/<Machine>/States/<State>.cs`.
- A trigger `<Trigger>` of the same machine (public or internal; check which folder it is in).
- The destination state `<Destination>` of the same machine.

## Run the tool

Build first (`dotnet build <SolutionDir>\<Solution>.slnx`), then:

```
& "<Tool>" -headless <common arguments> -pi "<state file>" --trigger <Trigger> --transition-to <Destination>
```

The tool also enables the trigger's using line. Then replace the stub's condition as step 4 describes.

## Fallback (tool without headless support)

1. Take the snippet from

   Template: PermitIfGuard_cs.sample

   substitute `@#$TriggerName@#$` = `<Trigger>` and `@#$TransitionToStateName@#$` = `<Destination>`.
2. Insert the snippet on the lines immediately **above** this marker in the state file, keeping the marker:

   Marker: // Injection Point 5, Do Not Delete or Modify

3. Enable the trigger's using: delete the text `//@#$PublicTriggers@#$` (public trigger) or
   `//@#$InternalTriggers@#$` (internal trigger) from the start of its using line. If it was already removed, do
   nothing.
4. The stub returns `true` (always transition). Replace it with the real condition from the approved model.

## Next

`statepipes-verify`: one `StateMachineForTest` test for this transition.
