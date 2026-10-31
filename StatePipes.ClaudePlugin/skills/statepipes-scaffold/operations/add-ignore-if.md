# add-ignore-if

External Tool: **Add IgnoreIf** (`-ii <state file>`). Generator: `IgnoreIfGaurdGeneratorTool.GenerateIgnoreIfGuard`.

## Inputs

- The state file `<Prefix>/StateMachines/<Machine>/States/<State>.cs`.
- A trigger `<Trigger>` of the same machine that this state must silently drop.

## Run the tool

Build first (`dotnet build <SolutionDir>\<Solution>.slnx`), then:

```
& "<Tool>" -headless <common arguments> -ii "<state file>" --trigger <Trigger>
```

The tool also enables the trigger's using line. Then set the condition as step 4 describes.

## Fallback (tool without headless support)

1. Take the snippet from

   Template: IgnoreIfGuard_cs.sample

   substitute `@#$TriggerName@#$` = `<Trigger>`.
2. Insert it on the lines immediately **above** this marker, keeping the marker:

   Marker: // Injection Point 5, Do Not Delete or Modify

3. Enable the trigger's using line as in `add-permit-if` step 3.
4. The stub returns `true` (always ignore). Replace with a condition if the trigger should be ignored only
   sometimes.

A trigger with no handler in the current state is already dropped (the framework logs "Trigger not set" at
verbose level). Use IgnoreIf to state that dropping it here is intended, or to drop it only under a condition.
