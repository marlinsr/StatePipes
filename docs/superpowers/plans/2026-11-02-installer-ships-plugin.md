# Installer Ships the Claude Code Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The StatePipes Installer installs and registers the matching `statepipes` Claude Code plugin, and New
StatePipes Sln / New StatePipes Service write the service `CLAUDE.md` and its rows.

**Architecture:** The ServiceCreatorTool embeds the plugin's `CLAUDE.md` template and writes it through a small
`ClaudeMdWriter`. The NSIS script copies the plugin as a local marketplace. `ServiceCreatorToolSetup` registers it by
running the `claude` CLI through an `ICommandRunner`, so the logic can be unit tested with a fake runner.

**Tech Stack:** .NET 10, C#, WinForms tool, NSIS, MSTest, Claude Code CLI (`claude plugin ...`).

**Spec:** `docs/superpowers/specs/2026-11-02-installer-ships-plugin-design.md`

## Global Constraints

- Marketplace and plugin are both named `statepipes`; the plugin id is `statepipes@statepipes`.
- Installed layout: `$INSTDIR\ClaudePlugin\.claude-plugin\marketplace.json`, `$INSTDIR\ClaudePlugin\statepipes\`
  (`.claude-plugin\plugin.json`, `README.md`, `skills\`). `tests\` is never installed.
- Registration never fails the install, and never stops the Visual Studio or NuGet setup.
- Manual commands (exact): `claude plugin marketplace add "<install folder>\ClaudePlugin"` and
  `claude plugin install statepipes@statepipes`.
- The plugin's `templates/CLAUDE.md` is the only copy of the template.
- A service row is `| <Component> | <Prefix> | <Prefix>/StatePipesModel.md |`, inserted above the line that starts
  `<!-- One row per service`.
- Before every dotnet command in PowerShell: `Remove-Item Env:MsBuildSDKsPath, Env:MSBuildEnableWorkloadResolver -ErrorAction SilentlyContinue`.
- Run `dotnet format` on changed projects before each commit (CLAUDE.md Rule 3).

## Rulings against the spec (from probing the installed CLI)

- `claude plugin marketplace list --json` returns an array of `{name, source, path?, repo?, installLocation}`; a local
  folder has `"source": "directory"` and `"path"`. `claude plugin list --json` returns `[{id, version, ...}]`.
- A plugin from a directory marketplace loads in place, and `marketplace add` / `plugin install` of something already
  present succeed with exit 0. So the spec's `marketplace update` / `plugin update` steps are dropped: the Installer
  overwriting the files is the update.
- Switching from GitHub: `plugin uninstall statepipes@statepipes` (when `plugin list` has it) runs before
  `marketplace remove statepipes`, or the old cached install would stay.

## Review Focus

1. Upgrading over an older install whose plugin had a file this version no longer has: the stale file must not
   stay. The install section runs `RMDir /r "$INSTDIR\ClaudePlugin"` before copying (Task 3 guard test).
2. The `path` from `marketplace list` differs from the computed folder only in case or a trailing backslash: it
   still counts as the install folder (Task 2 test).
3. `CLAUDE.md` with CRLF line endings: the inserted row uses the file's own line ending (Task 1 test).
4. `marketplace list --json` prints something that isn't the expected JSON: treated as a failed command (manual
   commands printed), never an exception (Task 2 test).
5. `claude` hangs (for example waiting for input): `ProcessCommandRunner` kills it after 120 s and returns exit
   code -1, so the Installer is never stuck (Task 2; covered by the failing-command test through the fake).

---

### Task 1: The tool writes CLAUDE.md and its service rows

**Files:**
- Create: `StatePipes.ServiceCreatorTool/ClaudeMdWriter.cs`
- Modify: `StatePipes.ServiceCreatorTool/StatePipes.ServiceCreatorTool.csproj` (linked embedded resource)
- Modify: `StatePipes.ServiceCreatorTool/BaseToolGenerator.cs` (extract `StatePipesVersion()`)
- Modify: `StatePipes.ServiceCreatorTool/SolutionGeneratorTool.cs` (`GenerateSolution`)
- Modify: `StatePipes.ServiceCreatorTool/ProjectGeneratorTool.cs` (`GenerateProject`)
- Modify: `StatePipes.ClaudePlugin/skills/statepipes-scaffold/templates/CLAUDE.md` (marker comment wording)
- Modify: `StatePipes.ClaudePlugin/skills/statepipes-scaffold/operations/new-solution.md`, `new-service.md`
- Test: `StatePipes.Test/ServiceCreatorTool/ClaudeMdWriterTest.cs` (new),
  `StatePipes.Test/ClaudePlugin/ScaffoldGoldenTest.cs` (existing, goes red then green),
  `StatePipes.Test/ServiceCreatorTool/HeadlessEndToEndTest.cs`

**Interfaces:**
- Produces:
  - `internal static class ClaudeMdWriter`
    - `public const string FileName = "CLAUDE.md";`
    - `public const string RowMarker = "<!-- One row per service";`
    - `public static string Render(string solutionName, string version)`: the template with `<Solution>` and
      `<version>` replaced, `<Prefix>` untouched.
    - `public static void WriteNew(string solutionDir, string solutionName, string version)`: writes
      `<solutionDir>\CLAUDE.md` only if it doesn't exist.
    - `public static bool AddServiceRow(string solutionDir, string prefix)`: true when a row was inserted.
  - `BaseToolGenerator.StatePipesVersion()` (`internal static string`), the value now computed inline for
    `@#$StatePipesVersion@#$`.

- [ ] **Step 1: Write the failing tests** in `ClaudeMdWriterTest` (`[TestCategory(TestCategories.Unit)]`, temp
  folder per test):
  - `Render_SubstitutesSolutionAndVersionOnly`: `Render("Packages.Acme.Widgets", "6.1.0")` starts with
    `# Packages.Acme.Widgets Project Memory`, contains `(StatePipes 6.1.0)`, contains
    `dotnet build Packages.Acme.Widgets.slnx`, contains `<Prefix>`, and contains neither `<Solution>` nor
    `<version>`.
  - `WriteNew_DoesNotOverwriteAnExistingFile`: existing `CLAUDE.md` with `keep` is still `keep`.
  - `AddServiceRow_InsertsAboveTheMarker`: after `WriteNew` then `AddServiceRow(dir, "Acme.Widgets.Pump")` returns
    true, and the line before the `RowMarker` line is `| Pump | Acme.Widgets.Pump | Acme.Widgets.Pump/StatePipesModel.md |`.
  - `AddServiceRow_SecondTimeAddsNothing`: the second call returns false and the row appears once.
  - `AddServiceRow_NoFileOrNoMarker_WritesNothing`: no file → false and no file is created. A file without the
    marker → false and the content is unchanged.
  - `AddServiceRow_KeepsCrlfLineEndings`: a CRLF file gets the row followed by `\r\n`, and contains no bare `\n`.

- [ ] **Step 2: Run them.** `dotnet test StatePipes.Test --filter "FullyQualifiedName~ClaudeMdWriterTest"`.
  Expected: compile FAIL, `ClaudeMdWriter` doesn't exist.

- [ ] **Step 3: Implement.**
  - Csproj: `<EmbeddedResource Include="..\StatePipes.ClaudePlugin\skills\statepipes-scaffold\templates\CLAUDE.md" LogicalName="StatePipes.ServiceCreatorTool.Resources.CLAUDE_md.sample" Link="Resources\CLAUDE_md.sample" />`.
  - `ClaudeMdWriter` reads that resource through `Assembly.GetManifestResourceStream`. A row's second cell is the
    text between the first and second `|`, trimmed. The row's line ending is `\r\n` if the file contains one,
    otherwise `\n`. `<Component>` is the last dot-separated part of the prefix.
  - `GenerateSolution`: after `GenerateSolutionFiles(helper)`, call
    `ClaudeMdWriter.WriteNew(slnDir, SolutionNameNoExtension, StatePipesVersion())`. After
    `ProjectGeneratorTool.GenerateProjectFiles(helper)`, call `ClaudeMdWriter.AddServiceRow(slnDir, projectName)`.
  - `GenerateProject`: after `GenerateProjectFiles(helper)`, call `ClaudeMdWriter.AddServiceRow(slnDir, projectName)`.
  - Template marker comment: `<!-- One row per service. New StatePipes Service adds rows above this line. -->`.

- [ ] **Step 4: Run the writer tests.** Same command. Expected: PASS.

- [ ] **Step 5: Run the golden tests.** `dotnet test StatePipes.Test --filter "FullyQualifiedName~ScaffoldGoldenTest"`.
  Expected: `NewSolution_CreatesExactlyTheFilesTheRecipeDeclares` FAILS with "Generated but not declared by recipe:
  [CLAUDE.md]".

- [ ] **Step 6: Update the recipes.**
  - `new-solution.md`: add `Creates: CLAUDE.md` after `Creates: ToolConfiguration.json`.
  - `new-solution.md` "Run the tool": the tool also writes `CLAUDE.md` with the first service's row.
  - `new-solution.md` "CLAUDE.md (both routes)": newer tools write it. If `<SolutionDir>/CLAUDE.md` doesn't exist
    after the tool runs, or on the by-hand route, copy the template as now.
  - `new-service.md` "CLAUDE.md": the tool adds the row when `CLAUDE.md` has the marker line. Check that the row is
    there, add it if it's missing, and write the file first if the solution has none.
  - Re-run the golden filter. Expected: PASS.

- [ ] **Step 7: E2E assertion.** In `HeadlessEndToEndTest`, after the new-service step, assert that
  `<SolutionDir>\CLAUDE.md` contains `| E2e | Acme.E2e | Acme.E2e/StatePipesModel.md |` and
  `| Pump | Acme.E2e.Pump | Acme.E2e.Pump/StatePipesModel.md |`. Run
  `dotnet test StatePipes.Test --filter "FullyQualifiedName~HeadlessEndToEndTest"`. Expected: PASS.

- [ ] **Step 8: Format and commit.**
  `dotnet format StatePipes.ServiceCreatorTool; dotnet format StatePipes.Test`, then
  `git commit -m "feat(tool): New StatePipes Sln writes CLAUDE.md and services add their rows"`.

### Task 2: ServiceCreatorToolSetup registers the plugin

**Files:**
- Create: `StatePipes.ServiceCreatorToolSetup/ICommandRunner.cs` (`ICommandRunner`, `CommandResult`, `ProcessCommandRunner`)
- Create: `StatePipes.ServiceCreatorToolSetup/ClaudePluginSetup.cs`
- Modify: `StatePipes.ServiceCreatorToolSetup/Program.cs`
- Modify: `StatePipes.ServiceCreatorToolSetup/StatePipes.ServiceCreatorToolSetup.csproj` (`<InternalsVisibleTo Include="StatePipes.Test" />`)
- Modify: `StatePipes.Test/StatePipes.Test.csproj` (`ProjectReference` to the Setup project)
- Test: `StatePipes.Test/ServiceCreatorToolSetup/ClaudePluginSetupTest.cs`

**Interfaces:**
- Produces:
  - `internal sealed record CommandResult(int ExitCode, string Output);`
  - `internal interface ICommandRunner { CommandResult Run(string fileName, IReadOnlyList<string> arguments); }`
  - `internal sealed class ProcessCommandRunner : ICommandRunner`: `ArgumentList`, stdout+stderr captured, killed
    after 120 s with `ExitCode` -1.
  - `internal sealed class ClaudePluginSetup(ICommandRunner runner, string? claudePath, string marketplaceDir, TextWriter output)`
    - `public void Register()`, `public void Remove()`
    - `public static string? FindClaude()`: `claude.exe` then `claude.cmd` in each `PATH` folder, then
      `%USERPROFILE%\.local\bin\claude.exe`.
    - `public static string InstalledMarketplaceDir()`:
      `Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ClaudePlugin"))`.

- [ ] **Step 1: Write the failing tests** with a `FakeRunner` that records each argument list and answers by the
  argument string (`"plugin marketplace list --json"` → JSON, and so on). Its default is `(0, "")`.
  `<dir>` is a temp folder per test that holds `statepipes\.claude-plugin\plugin.json` with `"version": "6.1.0"`.
  `Register` reads the version from that file. If the file can't be read, it prints the line without the version.
  - `Register_ClaudeNotFound_PrintsManualCommands`: `claudePath: null` → output contains
    `Claude Code not found; to add the StatePipes plugin later, run:`,
    `claude plugin marketplace add "<dir>"` and `claude plugin install statepipes@statepipes`, and no command ran.
  - `Register_NoMarketplace_AddsAndInstalls`: list `[]` → commands, in order, are `marketplace list --json`,
    `marketplace add <dir>`, `install statepipes@statepipes`. Output contains
    `StatePipes Claude Code plugin 6.1.0 installed.`.
  - `Register_GitHubMarketplace_SwitchesToLocal`: list has `statepipes` with `"source":"github"`, and `plugin list --json`
    has `statepipes@statepipes` → `uninstall statepipes@statepipes`, `marketplace remove statepipes`,
    `marketplace add <dir>`, `install statepipes@statepipes`, in that order.
  - `Register_AlreadyLocal_InstallsWithoutReAdding`: list has `statepipes`, `"source":"directory"`, and a `path` equal to the
    dir but lower-case with a trailing `\` → there's no `remove` and no `add`, and `install` runs.
  - `Register_FailingCommand_PrintsReasonAndManualCommandsAndStops`: `marketplace add` returns `(1, "boom")` →
    output contains `Claude Code plugin setup failed: claude plugin marketplace add`, `boom`, and the manual commands.
    `install` never ran.
  - `Register_ListNotJson_TreatedAsFailure`: list output `not json` → the failure line and the manual commands are
    printed, and there's no exception.
  - `Remove_LocalRegistration_UninstallsAndRemoves`: list is local and `plugin list` has the plugin → `uninstall`,
    then `marketplace remove statepipes`.
  - `Remove_GitHubRegistration_LeavesItAlone`: list has the github `statepipes` → only `marketplace list --json` ran.
  - `Remove_ClaudeNotFound_PrintsAndReturns`: output contains `Claude Code not found; StatePipes plugin not removed.`

- [ ] **Step 2: Run them.** `dotnet test StatePipes.Test --filter "FullyQualifiedName~ClaudePluginSetupTest"`.
  Expected: compile FAIL.

- [ ] **Step 3: Implement** `ICommandRunner.cs` and `ClaudePluginSetup.cs`.
  - Parse with `System.Text.Json` and catch `JsonException`; any parse failure counts as a failed command.
  - Compare paths with `Path.GetFullPath(p).TrimEnd('\\', '/')`, `OrdinalIgnoreCase`.
  - The failure line is `Claude Code plugin setup failed: claude <args joined by space>: <output trimmed>`.
  - `Remove` prints `StatePipes plugin removed from Claude Code.` on success.

- [ ] **Step 4: Wire `Program.cs`:**
  - With `--remove-plugin`: run `new ClaudePluginSetup(new ProcessCommandRunner(), ClaudePluginSetup.FindClaude(), ClaudePluginSetup.InstalledMarketplaceDir(), Console.Out).Remove()` and return.
  - Otherwise run `SetupEnvironment.Setup()` and then `Register()`, each in its own `try/catch`. A catch prints the
    exception message, so a failure in one never skips the other.

- [ ] **Step 5: Run the tests.** Same filter. Expected: PASS (9/9).

- [ ] **Step 6: Format and commit.**
  `dotnet format StatePipes.ServiceCreatorToolSetup; dotnet format StatePipes.Test`, then
  `git commit -m "feat(setup): register the StatePipes Claude Code plugin"`.

### Task 3: The Installer copies the plugin and runs registration

**Files:**
- Create: `StatePipes.ServiceCreatorTool.Installer/ClaudePlugin/.claude-plugin/marketplace.json`
- Modify: `StatePipes.ServiceCreatorTool.Installer/StatePipes.ServiceCreatorTool.Installer.nsi`
- Modify: `StatePipes.ClaudePlugin/README.md` (new "Installed by the StatePipes Installer" section)
- Test: `StatePipes.Test/ClaudePlugin/InstallerPluginTest.cs`

**Interfaces:**
- Consumes: `ServiceCreatorToolSetup.exe --remove-plugin` (Task 2); the marketplace folder
  `$INSTDIR\ClaudePlugin` that `InstalledMarketplaceDir()` computes.

- [ ] **Step 1: Write the failing tests** (Unit). Read the `.nsi` text, and split it into the install section (from
  `Section "MainSection"` to `SectionEnd`) and `Section Uninstall`.
  - `Installer_MarketplaceNamesThePlugin`: the Installer's `marketplace.json` has `name` `statepipes` and one plugin
    whose `name` equals `plugin.json`'s `name` and whose `source` is `./statepipes`.
  - `Installer_CopiesEveryPluginEntryButTests`: for each top-level entry of `StatePipes.ClaudePlugin` other than
    `tests`, the install section has a `File` line naming `..\StatePipes.ClaudePlugin\<entry>`. For a folder that is
    `File /r`; `.claude-plugin` is covered by its `plugin.json` line. No line names `StatePipes.ClaudePlugin\tests`.
  - `Installer_ClearsOldPluginBeforeCopying`: `RMDir /r "$INSTDIR\ClaudePlugin"` comes before the first `File` line
    naming `StatePipes.ClaudePlugin`.
  - `Installer_RegistersAfterCopyingThePlugin`: the `ExecWait` for `StatePipes.ServiceCreatorToolSetup.exe` comes after
    the plugin `File` lines.
  - `Uninstaller_RemovesPluginBeforeDeletingSetup`: in the uninstall section, an `ExecWait` with `--remove-plugin`
    comes before the first `Delete "$INSTDIR\ServiceCreatorToolSetup\`, and `RMDir /r "$INSTDIR\ClaudePlugin"` is present.

- [ ] **Step 2: Run them.** `dotnet test StatePipes.Test --filter "FullyQualifiedName~InstallerPluginTest"`.
  Expected: FAIL (marketplace file missing, `.nsi` lines missing).

- [ ] **Step 3: Implement.**
  - `marketplace.json`: copy of the root `.claude-plugin/marketplace.json` with `source` `./statepipes` and no
    `homepage`.
  - `.nsi` install section, before the `ServiceCreatorToolSetup` files:
    ```
    RMDir /r "$INSTDIR\ClaudePlugin"
    SetOutPath "$INSTDIR\ClaudePlugin\.claude-plugin"
    File "ClaudePlugin\.claude-plugin\marketplace.json"
    SetOutPath "$INSTDIR\ClaudePlugin\statepipes\.claude-plugin"
    File "..\StatePipes.ClaudePlugin\.claude-plugin\plugin.json"
    SetOutPath "$INSTDIR\ClaudePlugin\statepipes"
    File "..\StatePipes.ClaudePlugin\README.md"
    File /r "..\StatePipes.ClaudePlugin\skills"
    ```
  - First line of `Section Uninstall`:
    `ExecWait '"$INSTDIR\ServiceCreatorToolSetup\StatePipes.ServiceCreatorToolSetup.exe" --remove-plugin'`.
    Add `RMDir /r "$INSTDIR\ClaudePlugin"` next to the other `RMDir` lines.
  - Plugin `README.md`: the Installer installs the plugin from `Program Files (x86)\StatePipes\ClaudePlugin` and
    switches a GitHub registration to it, so the plugin then updates with the Installer. To go back to GitHub, run
    `/plugin uninstall statepipes@statepipes`, `/plugin marketplace remove statepipes`, then the two install commands
    already listed.

- [ ] **Step 4: Run the tests.** Same filter. Expected: PASS (5/5).

- [ ] **Step 5: Build the installer locally** if `makensis` is available:
  `dotnet build StatePipes.ServiceCreatorTool.Installer -c Release`. Expected: `StatePipes.ServiceCreatorTool.Installer.exe`
  is produced with no NSIS errors. If NSIS isn't installed, record a ruling and rely on CI's release build.

- [ ] **Step 6: Format and commit.**
  `dotnet format StatePipes.Test`, then
  `git commit -m "feat(installer): ship and register the Claude Code plugin"`.

### Task 4: Whole-suite verification

- [ ] **Step 1:** `dotnet build StatePipes.slnx` → 0 errors.
- [ ] **Step 2:** `dotnet test --settings Tests.runsettings --blame-hang-timeout 4m`, output redirected to the
  workspace → all pass. Report the count.
- [ ] **Step 3:** Hand the manual check to the user: run the built Installer on a machine with `claude`, check that
  `claude plugin list` shows `statepipes@statepipes` from the install folder, then run the uninstaller and check that
  it's gone.
