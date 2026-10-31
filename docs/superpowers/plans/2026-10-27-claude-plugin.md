# StatePipes Claude Code Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `StatePipes.ClaudePlugin/`, a Claude Code plugin whose skills guide design, scaffolding, extension and verification of single-service StatePipes microservices, with automated checks that keep the scaffold recipes in step with the ServiceCreatorTool templates.

**Architecture:** The plugin is markdown skills plus one verification script. Scaffold recipes are written in a small machine-readable format (`Template:`, `Marker:`, `Creates:` lines) that an MSTest class in `StatePipes.Test` parses and checks against `StatePipes.ServiceCreatorTool/Resources` and the real generators (`SolutionGeneratorTool`, `ProjectGeneratorTool`), which run without Visual Studio. Skills link to `docs/StateMachineComposition.md` and `docs/Paper.md` rather than copying them.

**Tech Stack:** Markdown skills (Claude Code plugin format), C# / MSTest 4.5.1 on `net10.0-windows` (existing `StatePipes.Test`), PowerShell for the service check script.

**Spec:** `docs/superpowers/specs/2026-10-27-claude-plugin-design.md` (issue #93)

## Global Constraints

- Domain-fit rule, verbatim: "If a microservice under development has distinct behavior based on state or mode, StatePipes should be used."
- Single-service design only; no multi-service design or boundary guidance.
- No change to `StatePipes.ServiceCreatorTool` production code, the Installer, or this repository's `CLAUDE.md`/`claude.md`.
- Recipes name templates and injection points; they never copy template contents.
- Skills link to `docs/StateMachineComposition.md` and `docs/Paper.md`; they do not duplicate them.
- Operations (exactly these twelve, kebab-case): `new-solution`, `new-service`, `new-proxy`, `update-proxy`, `add-state-machine`, `add-trigger`, `add-state`, `add-permit-if`, `add-permit-reentry-if`, `add-ignore-if`, `add-periodic`, `diagram`.
- Framework rules every skill must respect: no `.Result`/`.Wait()` in state machine code; fire-and-forget through `TaskWrapper`; run `dotnet format` before finishing; tests run with `dotnet test --settings Tests.runsettings`.
- Commits end with the `Co-Authored-By:` line for the model doing the work. Work stays on branch `issue-93-claude-plugin`; nothing is pushed until the user asks.

## Review Focus

- Line endings: git rewrites `.sample` and recipe files to CRLF on Windows; marker matching must not depend on `\n` vs `\r\n`.
- A recipe that cites a marker or template that was renamed or deleted must fail the test with a message naming the recipe file and the missing item, not a generic assertion.
- A solution name with the `Packages.` prefix (the form the tool's dialog asks for) must still produce the file tree the recipes declare.
- Tests must run on a machine with no Visual Studio and leave no temp directories behind.
- `domain-fit` on a borderline service (stateful data but a single operating mode) must ask the user rather than guess a verdict.

## File Structure

```
StatePipes.ClaudePlugin/
  .claude-plugin/plugin.json
  README.md
  scripts/Check-StatePipesService.ps1        # build, test, grep, format check for a service solution
  skills/
    statepipes-domain-fit/SKILL.md
    statepipes-domain-modeling/SKILL.md
    statepipes-conventions/SKILL.md
    statepipes-scaffold/SKILL.md             # index + recipe format + operation table
    statepipes-scaffold/operations/<op>.md   # twelve recipes
    statepipes-verify/SKILL.md
  tests/scenarios.md                         # runbook: greenfield, extension, rejection
StatePipes.Test/ClaudePlugin/
  RecipeParser.cs                            # parses recipe files
  RecipeParserTest.cs
  RecipeTemplateCheckTest.cs                 # recipes vs Resources/*.sample and BaseToolGenerator monikers
  ScaffoldGoldenTest.cs                      # recipes' Creates: vs real generator output
```

Recipe format (each `operations/<op>.md`): a one-line title, prose, then machine lines, one per line, anywhere in the file:
`Template: <FileName.sample>` · `Marker: <exact injection marker text>` · `Creates: <relative path, <Prefix> and <Machine> placeholders>`.

---

### Task 1: Plugin skeleton

**Files:**
- Create: `StatePipes.ClaudePlugin/.claude-plugin/plugin.json`, `StatePipes.ClaudePlugin/README.md`

**Interfaces:**
- Produces: plugin name `statepipes`; skills directory `StatePipes.ClaudePlugin/skills/` that later tasks fill.

- [ ] **Step 1:** Create `plugin.json` with `name` `statepipes`, `version` `0.1.0`, a one-sentence `description`, `author` `marlinsr`, `repository` `https://github.com/marlinsr/StatePipes`.
- [ ] **Step 2:** Create `README.md`: purpose, the skill flow (new service vs extension), how to load it locally (`claude --plugin-dir StatePipes.ClaudePlugin`), and a pointer to the spec.
- [ ] **Step 3:** Verify the manifest is valid JSON: `python -c "import json;json.load(open('StatePipes.ClaudePlugin/.claude-plugin/plugin.json'))"` exits 0.
- [ ] **Step 4:** Commit `feat(plugin): add StatePipes Claude Code plugin skeleton (#93)`.

---

### Task 2: Recipe parser

**Files:**
- Create: `StatePipes.Test/ClaudePlugin/RecipeParser.cs`
- Test: `StatePipes.Test/ClaudePlugin/RecipeParserTest.cs`

**Interfaces:**
- Produces: `internal sealed record Recipe(string Name, string FilePath, IReadOnlyList<string> Templates, IReadOnlyList<string> Markers, IReadOnlyList<string> Creates)`; `internal static class RecipeParser { public static Recipe Parse(string filePath); public static IReadOnlyList<Recipe> ParseDirectory(string operationsDirectory); }`. `Name` is the file name without `.md`.

- [ ] **Step 1: Write failing tests** (`[TestClass][TestCategory(TestCategories.Unit)]`, temp dir per test, cleaned in `[TestCleanup]`):
  - `Parse_ReadsTemplateMarkerAndCreatesLines`: file with `Template: A.sample`, `Marker: // Injection Point 5, Do Not Delete or Modify`, `Creates: <Prefix>/X.cs` → each list has that one value, trimmed.
  - `Parse_IgnoresProseAndBlankLines`: other lines produce no entries.
  - `Parse_HandlesCrlfLineEndings`: same content with `\r\n` yields identical values with no trailing `\r`.
  - `ParseDirectory_ReturnsOneRecipePerMarkdownFileSortedByName`.
- [ ] **Step 2:** Run `dotnet test StatePipes.Test --filter RecipeParserTest --settings Tests.runsettings`; expect compile failure/FAIL.
- [ ] **Step 3:** Implement `RecipeParser` per the signatures above (split on `\r?\n`, match lines starting with the three prefixes, trim the value).
- [ ] **Step 4:** Run the same command; expect 4 passed.
- [ ] **Step 5:** Commit `test(plugin): recipe parser (#93)`.

---

### Task 3: Scaffold skill and the twelve recipes

**Files:**
- Create: `StatePipes.ClaudePlugin/skills/statepipes-scaffold/SKILL.md` and `operations/<op>.md` for the twelve operations
- Test: `StatePipes.Test/ClaudePlugin/RecipeTemplateCheckTest.cs`

**Interfaces:**
- Consumes: `RecipeParser.ParseDirectory` (Task 2).
- Produces: a recipe directory the golden test (Task 7) and verify skill (Task 6) read. Test helper `internal static class PluginPaths { public static string RepoRoot(); public static string Operations; public static string Resources; }` in `RecipeTemplateCheckTest.cs`'s folder, locating the repo root by walking up from `AppContext.BaseDirectory` to the folder containing `StatePipes.slnx`.

- [ ] **Step 1: Write failing tests** in `RecipeTemplateCheckTest`:
  - `AllTwelveOperationsHaveARecipe`: the set of recipe names equals the twelve names in Global Constraints; message names any missing or extra.
  - `EveryTemplateNamedByARecipeExists`: each `Template:` value exists in `StatePipes.ServiceCreatorTool/Resources`; failure message is `"<recipe>.md names template '<x>' that does not exist"`.
  - `EveryMarkerNamedByARecipeIsAToolMoniker`: each `Marker:` value, compared after trimming and normalising line endings, equals a `*Moniker` string constant in `BaseToolGenerator.cs` (parse with a regex over `const string \w+Moniker = "(.*)";`) **and** appears in at least one `.sample` file; message names recipe and marker.
  - `StateMachineGuardRecipesNameTheirGuardTemplate`: `add-permit-if` → `PermitIfGuard_cs.sample`, `add-permit-reentry-if` → `PermitReentryIfGuard_cs.sample`, `add-ignore-if` → `IgnoreIfGuard_cs.sample`, `add-periodic` → all five `Periodic*_cs.sample`, `add-state` → `UnparentedState_cs.sample`, `FirstParentedState_cs.sample`, `ParentedState_cs.sample`.
- [ ] **Step 2:** Run `--filter RecipeTemplateCheckTest`; expect FAIL (no recipes yet).
- [ ] **Step 3:** Write the twelve recipes from `docs/VisualStudioExternalTools.md` and the generators (`*GeneratorTool.cs`). Each recipe states: inputs to collect, preconditions (what must already exist), `Creates:`/`Template:`/`Marker:` lines, the manual injection steps (which file, which marker, replace `@#$Placeholder@#$` values), and the follow-up (build, `add-permit-if` for the trigger, etc.). Markers: guards use `// Injection Point 5, Do Not Delete or Modify`; periodic uses points 6–9 (field, constructor, `OnEntry`, `OnExit`) plus 5; proxies use `Injection Point 1`/`2`/`4` as in `ProxyGeneratorTool.cs`; solution/Start/Stop scripts use `# Injection Point 1, Do Not Delete or Modify`. `diagram` and `update-proxy` carry no `Marker:` (update-proxy states injection is skipped for an existing proxy).
- [ ] **Step 4:** Write `SKILL.md`: frontmatter (`name: statepipes-scaffold`, description = "Use when creating or extending a StatePipes service: solution, service, state machine, state, trigger, guard, periodic timer, proxy, diagram"), the operation table (operation → External Tool menu title → recipe file), the recipe format, the rule "follow the recipe exactly; the headless tool will replace the recipe later", and the order of operations from the External Tools typical workflow.
- [ ] **Step 5:** Run `--filter RecipeTemplateCheckTest`; expect all pass.
- [ ] **Step 6:** Commit `feat(plugin): scaffold skill, twelve recipes, template drift check (#93)`.

---

### Task 4: Golden test against the real generators

**Files:**
- Test: `StatePipes.Test/ClaudePlugin/ScaffoldGoldenTest.cs`
- Modify: `operations/new-solution.md`, `operations/new-service.md` (add/adjust `Creates:` lines after observing real output)

**Interfaces:**
- Consumes: `RecipeParser`, `PluginPaths` (Task 3); `SolutionGeneratorTool(string solutionDir, string solutionFileName, bool usingRabbit).GenerateSolution()` and `ProjectGeneratorTool(string solutionDir, string solutionFileName).GenerateProject(string projectName)` (internal, visible to `StatePipes.Test`).

- [ ] **Step 1: Write failing tests** (temp dir, no Visual Studio, cleaned in `[TestCleanup]`):
  - `NewSolution_CreatesExactlyTheFilesTheRecipeDeclares`: generate a solution named `Packages.Acme.Widgets` (`Acme.Widgets.slnx` expected, see Review Focus) then assert the set of relative file paths equals the set of the recipe's `Creates:` lines.
  - `NewService_CreatesExactlyTheFilesTheRecipeDeclares`: after the solution, `GenerateProject("Acme.Widgets.Pump")`; the new files (tree difference) equal the `new-service` recipe's `Creates:` lines with `<Prefix>` = `Acme.Widgets.Pump` and `<Machine>` = the default state machine name.
  - `NewSolution_WorksWithPackagesPrefixAndWithout`: both forms produce the same tree apart from the solution file name.
- [ ] **Step 2:** Run `--filter ScaffoldGoldenTest`; expect FAIL listing the diff.
- [ ] **Step 3:** Update the two recipes' `Creates:` lines until the diff is empty. The real output is authoritative; if the tool and `docs/VisualStudioExternalTools.md` disagree, follow the tool and note the doc gap in the PR description.
- [ ] **Step 4:** Run `--filter ScaffoldGoldenTest`; expect pass. Confirm no `ScaffoldGoldenTest_*` directories remain in `%TEMP%`.
- [ ] **Step 5:** Commit `test(plugin): golden test of recipes against real generators (#93)`.

---

### Task 5: Conventions skill

**Files:**
- Create: `StatePipes.ClaudePlugin/skills/statepipes-conventions/SKILL.md`

**Interfaces:**
- Produces: the rules list that `domain-modeling`, `scaffold` and `verify` cite by section name: "Layout", "Naming", "Triggers", "States and lifecycle", "Guards", "Events and commands", "Framework rules".

- [ ] **Step 1:** Write the skill, with `description` "Use whenever reading or editing a StatePipes service". Sections, each ending with a link into `docs/StateMachineComposition.md` (section numbers 3–11 and 14) or `docs/Paper.md` rather than restating it:
  - Layout and naming exactly as `new-service` creates them (`StateMachines/<Machine>/States|Triggers|Triggers/Internal`, `Events`, `Builders`, `ValueObjects`).
  - Public versus internal triggers (public = reachable by proxy clients).
  - `OnEntry`/`OnExit` own all state side effects; `PermitReentryIf` returning `false` runs the body as a side effect without cycling the lifecycle.
  - Guard stubs return `true`; replace with real logic.
  - `SendCommand`/`PublishEvent`/`SendCurrentStatusAllStateMachines()` usage from `claude.md`.
  - Framework rules from Global Constraints.
- [ ] **Step 2:** Verify every `docs/...` link and `#anchor` in the skill resolves: run the link check in Task 8's script (until then, check by hand with `grep -n` that each heading exists).
- [ ] **Step 3:** Commit `feat(plugin): conventions skill (#93)`.

---

### Task 6: Domain-fit, domain-modeling and verify skills, plus the service check script

**Files:**
- Create: `skills/statepipes-domain-fit/SKILL.md`, `skills/statepipes-domain-modeling/SKILL.md`, `skills/statepipes-verify/SKILL.md`, `scripts/Check-StatePipesService.ps1`

**Interfaces:**
- Consumes: operation names (Task 3), convention section names (Task 5).
- Produces: `Check-StatePipesService.ps1 -SolutionDir <path> [-SkipFormat]` exiting 0 only when all checks pass; each failure prints one line `FAIL <check>: <detail>`. Checks: `dotnet build`; `dotnet test --settings Tests.runsettings`; no `.Result` or `.Wait()` in `*/StateMachines/**/*.cs`; `dotnet format --verify-no-changes` (skipped with `-SkipFormat`).

- [ ] **Step 1 (domain-fit):** Skill states the rule verbatim, then a checklist: does behavior change by state or mode; is a command valid only in some states; do lifecycles have entry/exit actions or timers. Outputs go/no-go with reasons; a no-go names what to build instead (plain service). For a borderline service (one operating mode, only configuration flags) it asks the user one question and does not decide alone. Worked example: pump controller (idle/running/fault) → go; a CRUD inventory API → no-go.
- [ ] **Step 2 (domain-modeling):** Skill produces a written model with fixed headings: Machines; States per machine (parent/child); Triggers (public/internal, payload); Events; Guards (kind + condition); Periodic timers; Proxies to other services (single service only: remote services are inputs, not designed here). Ends with an explicit approval gate: no scaffolding until the user approves the model. Maps each model element to the scaffold operation that creates it.
- [ ] **Step 3 (verify):** Skill lists: run `Check-StatePipesService.ps1`; one `StateMachineForTest` test per new transition following the pattern in `StateMachineDummyTests_cs.sample` (name the file, do not copy it); `diagram` operation; check against `docs/Paper.md`; do not report done until the script's output is shown.
- [ ] **Step 4:** Write the script. Verify against a generated solution: use the Task 4 temp solution approach by hand (`GenerateSolution` is test-only), or run against any local StatePipes service solution; confirm exit 0 on a clean one and a `FAIL no-blocking-calls` line after adding `x.Result` to a state file.
- [ ] **Step 5:** Commit `feat(plugin): domain-fit, domain-modeling, verify skills and service check script (#93)`.

---

### Task 7: Scenario runbook and spec alignment

**Files:**
- Create: `StatePipes.ClaudePlugin/tests/scenarios.md`
- Modify: `docs/superpowers/specs/2026-10-27-claude-plugin-design.md`

- [ ] **Step 1:** Write `scenarios.md`: three scenarios with the exact prompt, the skills expected to fire in order, and pass criteria (script exits 0; for rejection, verdict is no-go with reasons and no scaffolding):
  - Greenfield pump controller (idle, running, fault; start/stop/fault-reset triggers; periodic status poll).
  - Extension: add a `Maintenance` state with a `PermitIf` from `Idle` to an existing service.
  - Rejection: a CRUD inventory service.
- [ ] **Step 2:** Edit the spec: replace "runs in CI" with "runs under `dotnet test` (the repository has no pull-request CI; the release workflow runs only on merge to main)"; resolve open question 2 as "golden reference is generated at test time from the real generators, nothing checked in"; keep question 1 as is.
- [ ] **Step 3:** Run the full suite `dotnet test --settings Tests.runsettings --filter "TestCategory=Unit"`; expect all pass, then run `dotnet format --verify-no-changes StatePipes.Test`; expect exit 0.
- [ ] **Step 4:** Commit `docs(plugin): scenario runbook; align spec with repo CI reality (#93)`.

---

## Self-Review

- **Spec coverage:** five skills (Tasks 3, 5, 6); twelve operations (Task 3); recipe accuracy check (Tasks 2–3); golden test (Task 4); scenario tests (Task 7); keep repo `CLAUDE.md` untouched (Global Constraints); links not copies (Tasks 3, 5); open questions (Task 7). Not covered by design: running the scenarios, which are agent runs documented as a runbook.
- **Deviation from spec:** the spec says the recipe check "runs in CI"; the repo has no PR CI, so it runs under `dotnet test`. Task 7 fixes the spec wording.
- **Type consistency:** `Recipe`, `RecipeParser`, `PluginPaths` defined in Tasks 2–3 and used in Tasks 3–4 with the same names.
