---
name: statepipes-verify
description: Use before saying any StatePipes service change is done - formats, builds, tests (including a StateMachineForTest test per new transition), checks for blocking calls, and compares the result to the approved model.
---

# Verify a StatePipes change

Don't report a change as done until each step below has run and you have shown its output.

## 1. Test every new transition

For each new or changed row in the model's transitions table, write one test in `<Prefix>.Test`. Follow the
pattern in the generated `StateMachineDummyTests.cs` (template `StateMachineDummyTests_cs.sample`) and
`docs/StateMachineComposition.md` section 12. Reference the template; don't copy it.

- Create a `StateMachineForTest`, register mocks for state constructor dependencies, and call
  `ConfigureStateMachine<<Machine>, <StartState>>(...)`.
- Trap the expected event with `TrapEvent<TEvent>(...)`, call `Start()`, and send the trigger.
- Assert the resulting state with `IsCurrentState<TState>()`, assert the trapped event's payload, and for
  `IgnoreIf` rows assert that the state did not change.
- Categorise tests the way the generated `TestCategories` does. Tests that rely on the background command bus are
  `TimingCritical`.

## 2. Format, then run the checks

```powershell
dotnet format <Solution>.slnx
pwsh <plugin>/scripts/Check-StatePipesService.ps1 -SolutionDir <SolutionDir>
```

`<plugin>` is the `StatePipes.ClaudePlugin` folder. Freshly generated StatePipes code isn't `dotnet format`-clean,
so always format first. The script builds, runs the tests with `Tests.runsettings`, rejects `.Result` and
`.Wait()` under `StateMachines/`, and checks formatting. It prints `PASS`, or one `FAIL <check>: <detail>` line
per problem. Fix every `FAIL` and run it again.

## 3. Check against the model and the pattern

- Every state, trigger, handler row, event and timer in the approved model exists in the code, and the code has
  nothing that isn't in the model.
- Every entity in the model's domain model is in `<Prefix>/Entities/` and every value object is in
  `<Prefix>/ValueObjects/`, with matching namespaces. No domain type is anywhere else (see `statepipes-conventions`
  → Domain-Driven Design).
- Adding a state only added classes and handler methods. No existing transition changed unless the user approved
  that change (`docs/Paper.md` section 4).
- No leftover `@#$` placeholders, except the `//@#$PublicTriggers@#$` and `//@#$InternalTriggers@#$` using
  prefixes. No injection marker lines were deleted.

## 4. Diagram

Run the `diagram` operation: the user runs the Diagrammer in Visual Studio. Ask the user to compare the PDF with
the model. A mismatch is a bug in either the code or the model, so resolve it before finishing.

## Report

End with the script's `PASS` line, the test counts, and any part of step 4 that is still waiting on the user.
