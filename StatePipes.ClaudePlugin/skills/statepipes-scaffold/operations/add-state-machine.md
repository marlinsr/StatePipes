# add-state-machine

External Tool: **Add StateMachine** (`-m`). Generator: `StateMachineGeneratorTool.GenerateStateMachineFiles`.

## Inputs

- Service class library `<Prefix>`; new machine name `<Machine>` (PascalCase, unique in the service).

## Steps

1. Create the marker type and the first state:

   Template: StateMachine_cs.sample
   Template: TopLevelState_cs.sample
   Creates: <Prefix>/StateMachines/<Machine>/<Machine>.cs
   Creates: <Prefix>/StateMachines/<Machine>/States/TopLevelState.cs

2. Substitute `@#$ClassLibraryName@#$` = `<Prefix>` and `@#$StateMachineName@#$` = `<Machine>`.
3. `TopLevelState` implements `IFirstStateForStateMachine`: it is where the machine starts. Rename it only with a
   project-wide rename, and add child states under it with `add-state`.

Marker: // Injection Point 5, Do Not Delete or Modify

## Next

Check how the service's `Builders/DefaultSetup.cs` registers state machines (see `docs/StateMachineComposition.md`
section 9) and confirm the new machine is picked up; `statepipes-verify` will fail the build or tests otherwise.
