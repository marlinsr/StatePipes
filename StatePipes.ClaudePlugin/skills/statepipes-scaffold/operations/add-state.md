# add-state

External Tool: **Add State** (`-a`). Generator: `StateGeneratorTool.GenerateStateFile`.

## Inputs

- Service class library `<Prefix>`, state machine `<Machine>`, state name `<State>` (PascalCase).
- Parent: none (un-parented), or an existing state `<Parent>` of the same machine.

## Choose the template

| Case | Template |
|---|---|
| No parent | `UnparentedState_cs.sample` (inherits `BaseStateMachineState<<Machine>>`) |
| Parent, and no other state has this parent yet | `FirstParentedState_cs.sample` (adds `IFirstSubstate`: entered when the parent is entered) |
| Parent that already has a child | `ParentedState_cs.sample` |

To decide first-child, search `<Prefix>/StateMachines/<Machine>/States/*.cs` for
`ParentedBaseStateMachineState<<Machine>, <Parent>>`. None found means first child.

Template: UnparentedState_cs.sample
Template: FirstParentedState_cs.sample
Template: ParentedState_cs.sample
Creates: <Prefix>/StateMachines/<Machine>/States/<State>.cs

## Steps

1. Create the file from the chosen template.
2. Substitute `@#$ClassLibraryName@#$`, `@#$StateMachineName@#$`, `@#$StateName@#$`, and for parented states
   `@#$ParentStateName@#$`.
3. Leave these lines exactly as the template has them; later operations edit them:
   - `//@#$PublicTriggers@#$using ...Triggers;` and `//@#$InternalTriggers@#$using ...Triggers.Internal;`
     (a guard operation removes the `//@#$...@#$` prefix to enable the using).
   - The `// Injection Point 5` to `// Injection Point 9` comment lines.

Marker: // Injection Point 5, Do Not Delete or Modify
Marker: // Injection Point 6, Do Not Delete or Modify
Marker: // Injection Point 7, Do Not Delete or Modify
Marker: // Injection Point 8, Do Not Delete or Modify
Marker: // Injection Point 9, Do Not Delete or Modify

## Next

A state is reached only through a transition: add `add-permit-if` on the state(s) that lead here.
