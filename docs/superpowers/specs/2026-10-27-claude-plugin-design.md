# StatePipes Claude Code Plugin — Design

**Date:** 2026-10-27
**Branch:** `issue-93-claude-plugin`
**Issue:** [#93](https://github.com/marlinsr/StatePipes/issues/93)
**Component:** new `StatePipes.ClaudePlugin` (guidance only; no change to the framework or the ServiceCreatorTool)

## Goal

Guide Claude Code to design, build and extend a single StatePipes microservice for domains that benefit from the
state-scoped command handler pattern. The result of following the guidance matches what the Visual Studio External
Tools would produce, and respects the framework's rules.

## Domain-fit rule

If a microservice under development has distinct behavior based on state or mode, StatePipes should be used.

## Requirements (as agreed)

1. The plugin covers both new services (from a domain description) and extending existing services.
2. It lives in `StatePipes.ClaudePlugin/` in this repository, so guidance is versioned with the framework.
3. Scaffolding is expressed as named operations, one per External Tool. They start as recipes. When the headless
   ServiceCreatorTool (a later sub-project) lands, each recipe becomes a CLI call without changing the other skills.
4. Recipes name the template (`.sample`) and the injection points; they never copy template contents.
5. The guidance links to `docs/StateMachineComposition.md` and `docs/Paper.md` instead of duplicating them.
6. Single-service design only.

## Non-goals

- Guidance for concurrent multi-service design and service boundaries. **Future work**, tracked as a follow-up.
- A headless mode for the ServiceCreatorTool (sub-project 1, next).
- Installer changes, and generating a `CLAUDE.md` into new service solutions (sub-project 3).
- Changes to this repository's own `CLAUDE.md`. That file is for developers of the framework itself and stays as is.
  The `CLAUDE.md` for a *new service's* solution is a different file, generated in sub-project 3.

## Sequence of the three sub-projects

2 (this plugin) → 1 (headless ServiceCreatorTool) → 3 (Installer ships the plugin and writes a service `CLAUDE.md`).

## Skills

Flow for a new service: `domain-fit` → `domain-modeling` → `scaffold` + `conventions` → `verify`.
Flow for extension: `conventions` + `scaffold` → `verify`.

| Skill | Triggers when | Content |
|---|---|---|
| `statepipes-domain-fit` | Starting a new service or feature | Go/no-go on whether the service has distinct behavior by state or mode. Gives reasons either way. A stateless CRUD or pure data-pipeline service is a no-go. |
| `statepipes-domain-modeling` | A fit is confirmed | Turns a domain description into a written model: state machines, states (parent and child), triggers (public or internal), events, guards (`PermitIf`, `PermitReentryIf`, `IgnoreIf`), periodic timers, proxies to other services. The user approves the model before any code. |
| `statepipes-conventions` | Any StatePipes edit | Folder layout, naming, public versus internal triggers, `OnEntry`/`OnExit` rules, guard semantics (including `PermitReentryIf` returning `false` as a side-effect-only handler), framework rules (no `.Result`/`.Wait()`, `TaskWrapper`, `dotnet format`). |
| `statepipes-scaffold` | Creating or extending code | One operation per External Tool: `new-solution`, `new-service`, `new-proxy`, `update-proxy`, `add-state-machine`, `add-trigger`, `add-state`, `add-permit-if`, `add-permit-reentry-if`, `add-ignore-if`, `add-periodic`, `diagram`. Each lists its inputs, the files created, and the injection points edited. |
| `statepipes-verify` | Before saying work is done | Build, run tests, write a `StateMachineForTest` test per new transition, regenerate diagrams, check against `docs/Paper.md`. |

## Keeping recipes accurate

- Tests in `StatePipes.Test/ClaudePlugin` check the recipes against the repo: every `.sample` file a recipe names
  must exist, and every injection marker it cites must appear in the templates. They run under `dotnet test` (the
  repository has no pull-request CI; the release workflow runs only on merge to main), so a template change that
  breaks a recipe fails the test suite.
- When the headless tool lands, the script also verifies that the CLI flags a recipe uses exist.

## Testing the plugin

- **Golden test.** Run the real solution and service generators (they need no Visual Studio) in a scratch directory
  and compare the file tree with the files the `new-solution` and `new-service` recipes declare. A difference is a
  recipe bug.
- **Scenario tests** (fixed prompts, scratch solution):
  - Greenfield: a pump controller (idle, running, fault). Must build, pass generated tests, follow conventions.
  - Extension: add a state and a guarded transition to an existing service.
  - Rejection: a CRUD-style domain. `domain-fit` must answer no.
- **Pass criteria:** the solution builds, tests pass, no `.Result`/`.Wait()` in state machine code, and
  `dotnet format --verify-no-changes` is clean.

## Open questions

1. Worked examples for `domain-fit` beyond the pump controller (for example the OPC UA bridge). To be chosen during
   implementation planning; the first version ships with a short checklist and is refined from use.
2. ~~Where the golden-test reference output is stored.~~ Resolved: generated at test time from the real
   generators; nothing is checked in.

## Follow-ups

- Sub-project 1: headless mode for `StatePipes.ServiceCreatorTool`.
- Sub-project 3: Installer ships the plugin; `New StatePipes Sln` writes a service `CLAUDE.md`.
- Future: guidance for concurrent multi-service design and boundaries.
