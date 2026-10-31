# new-service

External Tool: **New StatePipes Service** (`-r <SolutionDir> -s <Solution>.slnx`). Generator:
`ProjectGeneratorTool.GenerateProjectFiles`.

Adds three projects (class library, `.Service` host, `.Test`) and their scripts to an existing solution.

## Inputs

- Solution directory and `<Solution>.slnx`.
- `<Prefix>` of the form `<Company>.<Product>.<Component>` (for example `Acme.Widgets.Pump`).
- Default state machine `<Machine>` = `<Component>StateMachine` (the tool names it; it is not asked).

## Preferred

Ask the user to run **Tools → New StatePipes Service** in Visual Studio, or create the files by hand as below.

## By hand

1. Insert above the solution file's marker the project entries from

   Template: SolutionProjectInjectionPoint1_sln.sample

   (the marker in the `.slnx` is wrapped as `<!--# Injection Point 1, Do Not Delete or Modify-->`).
2. Insert above the marker in `RunScript/Start.ps1` and `RunScript/Stop.ps1` the snippets from

   Template: ServiceStart_ps1.sample
   Template: ServiceStop_ps1.sample
   Marker: # Injection Point 1, Do Not Delete or Modify

3. Create every file below from its template, with the scaffold skill's moniker table. Generate new GUIDs for
   `@#$ServiceProjectGuid@#$`, `@#$ClassLibraryProjectGuid@#$`, `@#$TestProjectGuid@#$` (braced) and
   `@#$SecretsGuid@#$` (plain).

Template: Tests_runsettings.sample
Template: DockerStart_ps1.sample
Template: DockerStop_ps1.sample
Template: LocalImageBuild_ps1.sample
Template: ClassLibrary_csproj.sample
Template: ReadMe_md.sample
Template: DefaultSetup_cs.sample
Template: DefaultServiceConfiguration_cs.sample
Template: DummyEvent_cs.sample
Template: CurrentStatusEvent_cs.sample
Template: ProxyMonikers_cs.sample
Template: StateMachine_cs.sample
Template: ParentState_cs.sample
Template: DummyTrigger_cs.sample
Template: SendCurrentStatusCommand_cs.sample
Template: ProxyConnectionStatusTrigger_cs.sample
Template: appsettings_Development_json.sample
Template: appsettings_json.sample
Template: Dockerfile.sample
Template: Service_csproj.sample
Template: Program_cs.sample
Template: launchSettings_json.sample
Template: Test_csproj.sample
Template: DummyDependencyRegistration_cs.sample
Template: StateMachineDummyTests_cs.sample
Template: TestCategories_cs.sample

Creates: Tests.runsettings
Creates: RunScript/<Prefix>.Service.DockerStart.ps1
Creates: RunScript/<Prefix>.Service.DockerStop.ps1
Creates: BuildScript/<Prefix>.Service.LocalImageBuild.ps1
Creates: <Prefix>/<Prefix>.csproj
Creates: <Prefix>/<Prefix>.ReadMe.md
Creates: <Prefix>/Builders/DefaultSetup.cs
Creates: <Prefix>/Builders/DefaultServiceConfiguration.cs
Creates: <Prefix>/Events/DummyEvent.cs
Creates: <Prefix>/Events/CurrentStatusEvent.cs
Creates: <Prefix>/ValueObjects/ProxyMonikers.cs
Creates: <Prefix>/StateMachines/<Machine>/<Machine>.cs
Creates: <Prefix>/StateMachines/<Machine>/States/ParentState.cs
Creates: <Prefix>/StateMachines/<Machine>/Triggers/DummyTrigger.cs
Creates: <Prefix>/StateMachines/<Machine>/Triggers/SendCurrentStatusCommand.cs
Creates: <Prefix>/StateMachines/<Machine>/Triggers/Internal/ProxyConnectionStatusTrigger.cs
Creates: <Prefix>.Service/appsettings.Development.json
Creates: <Prefix>.Service/appsettings.json
Creates: <Prefix>.Service/Dockerfile
Creates: <Prefix>.Service/<Prefix>.Service.csproj
Creates: <Prefix>.Service/Program.cs
Creates: <Prefix>.Service/<Prefix>.Service.ReadMe.md
Creates: <Prefix>.Service/Properties/launchSettings.json
Creates: <Prefix>.Test/<Prefix>.Test.csproj
Creates: <Prefix>.Test/DummyDependencyRegistration.cs
Creates: <Prefix>.Test/StateMachineDummyTests.cs
Creates: <Prefix>.Test/TestCategories.cs

`DefaultSetup.cs`, `DefaultServiceConfiguration.cs` and `ProxyMonikers.cs` carry the proxy markers that
`new-proxy` later inserts above (`// Injection Point 1`, `2`, `4`).

## CLAUDE.md

Add a row for the service to the Services table in `<SolutionDir>/CLAUDE.md`, above the
`<!-- One row per service ... -->` line: `| <Component> | <Prefix> | <Prefix>/StatePipesModel.md |`. If the solution has
no `CLAUDE.md` (for example it was created before this rule), write it first, as `new-solution` describes.

## After

`dotnet build`, then `statepipes-verify`. The `Dummy*` files are examples: replace them with the approved model's
triggers, events and tests, and delete them once nothing references them.
