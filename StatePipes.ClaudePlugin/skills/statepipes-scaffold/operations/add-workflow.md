# add-workflow

ServiceCreatorTool operation **Add Workflow** (`-w`, headless; no Visual Studio External Tool). Generator:
`WorkflowGeneratorTool.GenerateWorkflow`.

A workflow is a state machine with many persisted instances, one per CorrelationId (for example one per shopping
cart), stored in PostgreSQL. Its states follow `statepipes-conventions` → Workflows.

## Inputs

- Service class library `<Prefix>`; new workflow name `<Workflow>` (PascalCase, unique in the service).

## Run the tool

```
& "<Tool>" -headless <common arguments> -w --workflow-name <Workflow>
```

It creates the three files below and, for the service's first workflow, makes both insertions in step 3 (it skips
an insertion that is already there). Then build, so later operations can see `<Workflow>`.

## Fallback (tool without headless support)

1. Create the marker type, the first state and a final state:

   Template: Workflow_cs.sample
   Template: WorkflowTopLevelState_cs.sample
   Template: WorkflowFinishedState_cs.sample
   Creates: <Prefix>/StateMachines/<Workflow>/<Workflow>.cs
   Creates: <Prefix>/StateMachines/<Workflow>/States/TopLevelState.cs
   Creates: <Prefix>/StateMachines/<Workflow>/States/FinishedState.cs

2. Substitute `@#$ClassLibraryName@#$` = `<Prefix>` and `@#$StateMachineName@#$` = `<Workflow>`.
3. Only if `<Prefix>/Builders/DefaultServiceConfiguration.cs` does not yet contain `new WorkflowStoreConfig(`:
   - insert the line from

     Template: DefaultServiceConfiguration_AddWorkflowStore.sample

     immediately above `Type setup = typeof(DefaultSetup)!;`, and
   - change the last `ServiceConfiguration` argument `new ServiceArgs(null));` to `new ServiceArgs(null), workflowStore);`,
     which gives `new ServiceArgs(null), workflowStore)`.

   Only if `<Prefix>.Service/<Prefix>.Service.csproj` does not yet contain `statepipes.postgres-db.client.p12`:
   append `&#xD;&#xA;` followed by this line to the end of the PostBuild `<Exec Command="...">` value (before its
   closing `" />`), so bare-metal Debug runs find the PostgreSQL client certificate:

   ```
   if exist $(ProgramData)\SmallStep\step-ca\statepipes.postgres-db.client.p12 copy $(ProgramData)\SmallStep\step-ca\statepipes.postgres-db.client.p12 $(OutDir)\certs\statepipes.postgres-db.client.p12
   ```

4. `TopLevelState` implements `IFirstStateForStateMachine`: every new instance starts there. `FinishedState`
   implements `IFinalWorkflowState`: entering it deletes the instance. Rename either only with a project-wide rename.

Marker: // Injection Point 5, Do Not Delete or Modify

## Next

Nothing has to be registered: the service host finds every `IWorkflow` in the class library. The service needs the
PostgreSQL infrastructure (`RunScript/DockerInfrastructureStart.ps1` starts `postgres-db`). Add states with
`add-state` (`--final true` for further final states) and triggers with `add-trigger`; test each transition with
`WorkflowForTest<<Workflow>>`.
