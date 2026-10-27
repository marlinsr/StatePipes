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

Nothing has to be registered. The service host finds every `IStateMachine` in the class library automatically
(`AllStateMachineContainerSetup`), together with its states and triggers. **Don't** add
`BaseStateMachineContainerSetup` code from `docs/StateMachineComposition.md` section 9 to `Builders/DefaultSetup.cs`,
because that registers the machine twice. Next, add states with `add-state` and triggers with `add-trigger`.
