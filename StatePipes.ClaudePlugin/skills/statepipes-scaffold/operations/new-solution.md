# new-solution

External Tool: **New StatePipes Sln** (no arguments). Generator: `SolutionGeneratorTool.GenerateSolution`.

Creates a solution folder and its build and run scripts, then adds a first service exactly as `new-service` does
(service name = the product part of the solution name).

## Preferred

Ask the user to run **New StatePipes Sln** (Start menu or Tools menu). It asks whether the broker is RabbitMQ or
Kafka, the repo root, and a name such as `Packages.<Company>.<Product>`. The entered name, prefix included, becomes
both the folder and the solution: `Packages.<Company>.<Product>/Packages.<Company>.<Product>.slnx`. `<Company>` and
`<Product>` are taken from the name with the `Packages.` prefix removed. The tool then opens Visual Studio.

## By hand (no Visual Studio)

`<Solution>` is the entered name without `.slnx`. Create these files under `<SolutionDir>` from their templates,
with the substitutions in the scaffold skill's placeholder table (`@#$SolutionName@#$` = `<Solution>`):

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
Creates: ToolConfiguration.json

`ToolConfiguration.json` has no template: write `{"UseRabbitMQ": true}` (or `false` for Kafka). `new-service`
reads it to choose the broker.

`SetupAndRunInstructions_pdf.sample` is binary: copy it unchanged. The solution file and the two run scripts carry
the marker that `new-service` inserts above:

Marker: # Injection Point 1, Do Not Delete or Modify

Then run `new-service` with `<Prefix>` = `<Company>.<Product>`.

## CLAUDE.md (both routes)

Neither the tool nor the templates above create a `CLAUDE.md`, so always write one. Copy `templates/CLAUDE.md` from
the `statepipes-scaffold` skill folder (next to its `SKILL.md`) to `<SolutionDir>/CLAUDE.md`. Replace `<Solution>`
with the solution name and `<version>` with the StatePipes version, and leave `<Prefix>` as written. If a
`CLAUDE.md` already exists, merge the template's sections into it instead of overwriting it. `new-service` then adds
the service's row to the Services table.

## After

Hosts-file entries are needed for `statepipes.explorer`, `step-ca`, `amqp09-broker`, `kafka-broker` and `keycloak`
(the tool reminds the user). See `docs/SETUP.md`.
