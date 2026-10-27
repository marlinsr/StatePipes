# add-ignore-if

External Tool: **Add IgnoreIf** (`-ii <state file>`). Generator: `IgnoreIfGaurdGeneratorTool.GenerateIgnoreIfGuard`.

## Inputs

- The state file `<Prefix>/StateMachines/<Machine>/States/<State>.cs`.
- A trigger `<Trigger>` of the same machine that this state must silently drop.

## Steps

1. Take the snippet from

   Template: IgnoreIfGuard_cs.sample

   substitute `@#$TriggerName@#$` = `<Trigger>`.
2. Insert it on the lines immediately **above** this marker, keeping the marker:

   Marker: // Injection Point 5, Do Not Delete or Modify

3. Enable the trigger's using line as in `add-permit-if` step 3.
4. The stub returns `true` (always ignore). Replace with a condition if the trigger should be ignored only
   sometimes.

Use IgnoreIf when a trigger is expected in this state and must not be treated as an error; a trigger with no
handler in the current state is otherwise unhandled.
