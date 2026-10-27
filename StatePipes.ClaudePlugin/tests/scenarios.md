# Scenario runbook

Three end-to-end checks of the plugin, each run by a person driving Claude Code with the plugin loaded
(`claude --plugin-dir StatePipes.ClaudePlugin`). Run them in a scratch directory after changing any skill.
They complement the automated tests in `StatePipes.Test/ClaudePlugin/`, which check the recipes against the
ServiceCreatorTool but cannot judge the skills' guidance.

Record each run in the PR description: date, plugin version, pass or fail per criterion.

## 1. Greenfield: pump controller

**Setup:** an empty scratch folder, opened in Claude Code with the plugin loaded.

**Prompt:**

> Build a StatePipes service `Acme.Plant.Pump` for a pump controller. The pump is idle until it receives Start
> with a target RPM, then runs. While running it polls its pressure sensor every 500 ms and publishes a status
> event. On over-pressure it faults and stops. A fault can be reset only by a ResetFault command, which returns it
> to idle. Stop returns a running pump to idle. Start is ignored while running.

**Expected skills, in order:** `statepipes-domain-fit` → `statepipes-domain-modeling` → `statepipes-scaffold` with
`statepipes-conventions` → `statepipes-verify`.

**Pass criteria:**

- Domain-fit verdict is Go, naming Idle, Running and Faulted.
- A model with the domain-modeling headings is presented, and nothing is scaffolded until it is approved.
- The scaffold follows the recipes: `new-solution` or `new-service`, then states, triggers, guards and an
  `add-periodic` on Running with period 500.
- One `StateMachineForTest` test per transition row.
- `Check-StatePipesService.ps1` prints `PASS`.

## 2. Extension: add a maintenance state

**Setup:** the solution produced by scenario 1, in a new Claude Code session.

**Prompt:**

> Add a Maintenance state to the pump. From Idle, an EnterMaintenance command moves it to Maintenance; while in
> maintenance Start is ignored; ExitMaintenance returns it to Idle.

**Expected skills:** `statepipes-conventions` + `statepipes-scaffold` → `statepipes-verify` (no domain-fit).

**Pass criteria:**

- `add-state`, `add-trigger` (EnterMaintenance and ExitMaintenance, both public), `add-permit-if` on Idle and on
  Maintenance, and `add-ignore-if` for Start on Maintenance.
- `git diff` shows existing states changed only by added handler methods and enabled using lines, with no edits
  to existing guard bodies.
- New tests for the three new rows, and `Check-StatePipesService.ps1` prints `PASS`.

## 3. Rejection: CRUD service

**Prompt:**

> Build a StatePipes service for an inventory API: create, read, update and delete items, and list items by
> category.

**Pass criteria:**

- Domain-fit verdict is No-go, with reasons quoting the description.
- A plain .NET service is suggested, and no StatePipes files are created.
