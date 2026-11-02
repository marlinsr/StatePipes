# Installer ships the Claude Code plugin — design

Sub-project 3 of the Claude Code plugin work (#93, #94; sub-project 1 was #95).

## Goal

Anyone who runs the StatePipes Installer gets the `statepipes` Claude Code plugin that matches the tool version it
installed, with no `/plugin` commands to type. A solution created with **New StatePipes Sln**, from Visual Studio or
headless, gets the service `CLAUDE.md`, and **New StatePipes Service** adds its row.

## Decisions

- The Installer copies the plugin into the install folder as a local marketplace and registers it with Claude Code
  when `claude` is found. Otherwise it prints the commands to run.
- The marketplace keeps the name `statepipes`. A `statepipes` marketplace registered from anywhere else, such as
  GitHub, is switched to the install folder. The plugin then updates when the Installer runs, not from GitHub.
- Registration never fails the install.
- The plugin's `templates/CLAUDE.md` stays the only copy of the template. The tool embeds it.
- Plugin recipes check what the tool produced rather than its version, so they need no new version gate.

## Installed files

Under `$INSTDIR` (`Program Files (x86)\StatePipes`):

```
ClaudePlugin\.claude-plugin\marketplace.json   new file in StatePipes.ServiceCreatorTool.Installer
ClaudePlugin\statepipes\.claude-plugin\plugin.json
ClaudePlugin\statepipes\README.md
ClaudePlugin\statepipes\skills\...             File /r of StatePipes.ClaudePlugin\skills
```

`tests\` is not installed. `marketplace.json` has `"name": "statepipes"` and one plugin, `"name": "statepipes"`,
`"source": "./statepipes"`, with the same description as the repository's root `marketplace.json`. `plugin.json` is
already stamped with the release version by `release.yml`, so the installed plugin matches the installed tool.

The uninstaller removes the folder with `RMDir /r "$INSTDIR\ClaudePlugin"`.

## Registration (ServiceCreatorToolSetup)

The Installer already runs `ServiceCreatorToolSetup.exe` after copying files. It gains a plugin step, which runs
after the NuGet and Visual Studio setup and whose failure doesn't affect them. The marketplace folder is
`<folder of ServiceCreatorToolSetup.exe>\..\ClaudePlugin`, so the step needs no argument.

Claude Code is run through an `ICommandRunner` (run a program with arguments, return exit code and output), so the
steps can be tested with a fake runner.

1. Find `claude`: `claude.exe` (or `claude.cmd`) on `PATH`, else `%USERPROFILE%\.local\bin\claude.exe`. Not found:
   print `Claude Code not found; to add the StatePipes plugin later, run:` followed by the two commands below, and
   stop.
2. `claude plugin marketplace list --json`. If a `statepipes` marketplace exists and its source isn't the install
   folder, run `claude plugin marketplace remove statepipes`.
3. If no `statepipes` marketplace points at the install folder, run
   `claude plugin marketplace add "<install folder>\ClaudePlugin"`. Otherwise run
   `claude plugin marketplace update statepipes`.
4. `claude plugin install statepipes@statepipes`. If the plugin was already installed, run
   `claude plugin update statepipes@statepipes` instead.
5. Print `StatePipes Claude Code plugin <version> installed.`

Any non-zero exit code prints `Claude Code plugin setup failed: <command>: <output>` and then the manual commands,
and the step stops. The manual commands are:

```
claude plugin marketplace add "<install folder>\ClaudePlugin"
claude plugin install statepipes@statepipes
```

The exact JSON fields of `marketplace list --json` and the `update` subcommand are confirmed against the installed
CLI during planning. If the CLI lacks one of them, the plan uses the closest equivalent (for example remove and
add, or uninstall and install) and records it.

### Uninstall

`ServiceCreatorToolSetup.exe --remove-plugin`, run by the uninstaller before it deletes the Setup files. It finds
`claude` as above and, only when the `statepipes` marketplace's source is the install folder, runs
`claude plugin uninstall statepipes@statepipes` and `claude plugin marketplace remove statepipes`. A registration
from anywhere else is left alone. Not found or any failure prints a line and exits 0. With `--remove-plugin`, the
NuGet and Visual Studio setup does not run.

## CLAUDE.md from the tool

- `StatePipes.ServiceCreatorTool.csproj` embeds
  `..\StatePipes.ClaudePlugin\skills\statepipes-scaffold\templates\CLAUDE.md` as a linked `EmbeddedResource` with
  its own `LogicalName`.
- **New solution:** `SolutionGeneratorTool` writes `<SolutionDir>\CLAUDE.md` before it creates the first service.
  `<Solution>` becomes the solution name and `<version>` the tool's product version. `<Prefix>` is left as written.
  This works with and without `-headless`.
- **New service:** `ProjectGeneratorTool` inserts `| <Component> | <Prefix> | <Prefix>/StatePipesModel.md |` on
  its own line above the line that starts `<!-- One row per service`. It does this only when `CLAUDE.md` exists, has
  that line, and has no row whose second cell is `<Prefix>`. Otherwise it writes nothing, so an older solution
  without `CLAUDE.md` doesn't get one by surprise. `<Component>` is the last dot-separated part of `<Prefix>`. A new
  solution's first service gets its row through the same code.

## Plugin recipes

- `new-solution`: after the tool runs, write `CLAUDE.md` from the template only if it doesn't exist. The by-hand
  fallback still writes it.
- `new-service`: add the row only if it's missing.
- `README.md` (plugin): a section saying the Installer installs the plugin, that it then updates with the Installer,
  and how to go back to the GitHub marketplace.
- `SKILL.md` needs no new version gate.

## Testing

All tests are MSTest in `StatePipes.Test`.

- **CLAUDE.md (Unit):** the template with its values substituted. Row insertion when the row is missing; nothing
  written when there's no file, no marker line, or the row is already there.
- **Plugin registration (Unit, fake runner):** `claude` not found prints the manual commands; a GitHub
  `statepipes` marketplace is removed and the local one added; an existing local one is updated; an installed
  plugin is updated, not installed; a failing command prints the manual commands and runs nothing after it;
  `--remove-plugin` acts only on a local registration.
- **Installer guards (Unit):** every file under `StatePipes.ClaudePlugin` except `tests\` is covered by the `.nsi`'s
  `File` lines. The Installer's `marketplace.json` names the plugin in `plugin.json` with source `./statepipes`. The
  uninstaller runs `--remove-plugin` before deleting the Setup files and removes `ClaudePlugin`.
- **Headless end-to-end (Integration):** the new solution has `CLAUDE.md` with rows for `Acme.E2e` and
  `Acme.E2e.Pump`.
- **Manual:** run the built Installer once on a machine with `claude`, then check `claude plugin list`, and run the
  uninstaller.

## Out of scope

- Multi-service design guidance.
- Changing the External Tools or NuGet setup.
- Writing `CLAUDE.md` into solutions that don't have one.
