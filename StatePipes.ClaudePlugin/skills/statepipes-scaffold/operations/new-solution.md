# new-solution

External Tool: **New StatePipes Sln** (no arguments). Generator: `SolutionGeneratorTool.GenerateSolution`.

Creates a solution folder and its build and run scripts, then adds a first service exactly as `new-service` does
(service name = the product part of the solution name).

## Preferred

Ask the user to run **New StatePipes Sln** (Start menu or Tools menu). It asks for the repo root and a name of the
form `Packages.<Company>.<Product>`. The folder keeps the `Packages.` prefix; the solution is
`<Company>.<Product>.slnx`. The tool writes `StatePipesToolConfig` (RabbitMQ or Kafka) and opens Visual Studio.

## By hand (no Visual Studio)

Create these files under `<SolutionDir>` from their templates, with the substitutions in the scaffold skill's
moniker table (`@#$SolutionName@#$` = `<Company>.<Product>`):

Template: SetupAndRunInstructions_pdf.sample
Template: SolutionInfo_proj.sample
Template: _dockerignore.sample
Template: _gitignore.sample
Template: Solution_slnx.sample
Template: DockerInfrastructureStart_ps1.sample
Template: DockerInfrastructureStop_ps1.sample
Template: Start_ps1.sample
Template: Stop_ps1.sample
Template: NugetConfig_xml.sample

Creates: SetupAndRunInstructions.pdf
Creates: SolutionInfo.proj
Creates: .dockerignore
Creates: .gitignore
Creates: <Solution>.slnx
Creates: RunScript/DockerInfrastructureStart.ps1
Creates: RunScript/DockerInfrastructureStop.ps1
Creates: RunScript/Start.ps1
Creates: RunScript/Stop.ps1
Creates: BuildScript/NugetConfig.xml

`SetupAndRunInstructions_pdf.sample` is binary: copy it unchanged. The solution file and the two run scripts carry
the marker that `new-service` inserts above:

Marker: # Injection Point 1, Do Not Delete or Modify

Then run `new-service` with `<Prefix>` = `<Company>.<Product>`.

## After

Hosts-file entries are needed for `statepipes.explorer`, `step-ca`, `amqp09-broker`, `kafka-broker` and `keycloak`
(the tool reminds the user). See `docs/SETUP.md`.
