# Headless ServiceCreatorTool and Diagrammer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every ServiceCreatorTool operation and the Diagrammer run with `-headless` and take every input as an argument,
and the `statepipes-scaffold` plugin skill runs them instead of hand-building files.

**Architecture:** One `IToolInput` (implemented by `ToolInput` over parsed `--` arguments and an `IDialogPrompt`) replaces
every `SelectionDialog` call in the `Create…` methods, which return a `ToolResult`. `Program.Main` returns an exit code and
prints `Finished!!!` only on success. The Diagrammer gets `-headless` and `--out`.

**Tech Stack:** .NET 10 (`net10.0-windows`), C#, WinForms (dialogs only), MSTest.

**Spec:** `docs/superpowers/specs/2026-10-31-headless-service-creator-tool-design.md`

## Global Constraints

- Without `-headless`, every Visual Studio External Tool behaves as today, except that a canceled dialog prints
  `Action canceled.` instead of `Finished!!!`.
- Generated file contents and templates do not change. Existing golden tests in `StatePipes.Test/ClaudePlugin/` stay
  unchanged and must still pass.
- Missing input in headless mode prints exactly `Headless mode needs <arg>.` and writes no file.
- Bad choice prints `<arg> <value> not found; one of: A, B, C`.
- Success: `Finished!!!` and exit code 0. Every failure: exit code 1, no `Finished!!!`.
- Argument names are exactly: `--repo`, `--transport`, `--solution-name`, `--prefix`, `--statemachine-name`,
  `--statemachine`, `--state`, `--parent-state`, `--trigger-name`, `--trigger-scope`, `--event`, `--trigger`,
  `--transition-to`, `--timer-name`, `--timer-period`, `--moniker`, `--dll`, `--broker`, `--exchange`, `--cert`,
  `--certpw`, `--timeout`, and for the Diagrammer `--out`.
- New test classes use `[TestCategory(TestCategories.Unit)]`, except the end-to-end class, which uses
  `TestCategories.Integration`. CI runs only Unit.
- Before every `dotnet` command, run `Remove-Item Env:MsBuildSDKsPath, Env:MSBuildEnableWorkloadResolver -ErrorAction SilentlyContinue`
  (the shell has a stale SDK path).
- Run `dotnet format StatePipes.slnx --include <changed files>` before each commit. Formatting the whole solution has
  existing failures in other projects.

## Review Focus

1. **A `--name` given as the last argument with no value.** Today `args[++i]` throws `IndexOutOfRange`. The tool should
   print `--state needs a value.` and exit 1. Covered in Task 1.
2. **A name that already exists.** `SaveTextFile` overwrites, so a headless `--state Idle` would replace an existing
   `Idle.cs`. The Create methods should refuse with `<relative path> already exists.` and write nothing. Covered in
   Tasks 3 and 5 (unit) and Task 7 (end to end).
3. **Names that aren't C# identifiers** (`--state "Idle State"`, `--event 1st`). These generate code that doesn't
   compile. `Name()` should reject them with `<arg> must be a C# identifier: <value>`. Covered in Task 1.
4. **A stale build.** A trigger added a moment ago isn't in the DLL until the next build, so `--trigger Start` fails.
   The not-found message for build-derived choices should add `. If it was just added, build the solution first.`
   Covered in Task 1 (the hint) and Task 4 (it's used).
5. **Relative state-file paths** (`-pi Acme.Widgets\StateMachines\...\Idle.cs` run from the solution folder). They
   should resolve against the current directory. Covered in Task 2 (`Path.GetFullPath`) and Task 7 (the end-to-end test
   uses a relative path).

## File structure

| File | Responsibility |
|---|---|
| `StatePipes.ServiceCreatorTool/IToolInput.cs` (new) | the input interface |
| `StatePipes.ServiceCreatorTool/IDialogPrompt.cs` (new) | dialog seam, plus `SelectionDialogPrompt`, which wraps `SelectionDialog` and `FolderBrowserDialog` |
| `StatePipes.ServiceCreatorTool/ToolInput.cs` (new) | argument-then-headless-then-dialog logic and validation |
| `StatePipes.ServiceCreatorTool/ToolArguments.cs` (new) | the known `--` names, plus parsing `--name value` pairs |
| `StatePipes.ServiceCreatorTool/ToolResult.cs` (new) | the `Create…` result |
| `StatePipes.ServiceCreatorTool/ProxyInputs.cs` | deleted in Task 2 |
| each `*GeneratorTool.cs`, `StateMachineTypesHelper.cs`, `ProxyGenerator*.cs`, `Program.cs` | changed to use `IToolInput` and `ToolResult` |
| `StatePipes.Diagrammer/Program.cs` | `-headless`, `--out`, exit code |
| `StatePipes.Test/ServiceCreatorTool/ToolInputTest.cs`, `ToolArgumentsTest.cs`, `ProxyToolInputTest.cs` (replaces `ProxyInputsTest.cs`), `CreateOperationsTest.cs` | unit tests |
| `StatePipes.Test/ServiceCreatorTool/HeadlessEndToEndTest.cs` | integration test |
| `StatePipes.ClaudePlugin/skills/statepipes-scaffold/**` | recipes run the tool |
| `docs/VisualStudioExternalTools.md` | Headless Mode section |

---

### Task 1: ToolInput, ToolArguments, ToolResult

**Files:**
- Create: `StatePipes.ServiceCreatorTool/IToolInput.cs`, `IDialogPrompt.cs`, `ToolInput.cs`, `ToolArguments.cs`, `ToolResult.cs`
- Test: `StatePipes.Test/ServiceCreatorTool/ToolInputTest.cs`, `StatePipes.Test/ServiceCreatorTool/ToolArgumentsTest.cs`

**Interfaces (Produces):**
```csharp
internal interface IToolInput
{
    bool Headless { get; }
    bool Supplied(string arg);
    string? Text(string arg, string prompt, string initial = "");            // non-empty, otherwise null
    string? Name(string arg, string prompt);                                  // Text plus C# identifier check
    string? Choose(string arg, IReadOnlyList<string> options, string prompt, bool fromBuild = false);
    string? File(string arg, string prompt, string filter, string? initialDirectory = null);
    string? Folder(string arg, string prompt);
    bool? Confirm(string arg, string trueValue, string falseValue, string prompt);
    int? PositiveInt(string arg, string prompt);
    IToolInput WithDefault(string arg, string value);                         // value used only if arg not supplied
}
internal interface IDialogPrompt
{
    string? Text(string prompt, string initial);      // null when canceled
    string? Choose(IReadOnlyList<string> options, string prompt);
    string? File(string prompt, string filter, string? initialDirectory);
    string? Folder(string prompt);
    bool? Confirm(string prompt);                     // null never returned by the WinForms prompt (yes/no)
}
internal sealed class SelectionDialogPrompt : IDialogPrompt { /* forwards to SelectionDialog / FolderBrowserDialog */ }
internal sealed class ToolInput(IReadOnlyDictionary<string, string> args, bool headless, IDialogPrompt dialogs) : IToolInput
{
    public static ToolInput Dialogs() => new(new Dictionary<string, string>(), false, new SelectionDialogPrompt());
}
internal static class ToolArguments
{
    public static readonly IReadOnlySet<string> Known; // the Global Constraints list without --out
    // Collects every "--name value". Returns null and prints the error for an unknown --name ("Unknown argument --x.")
    // or a missing value ("--x needs a value."). Single-dash flags are ignored here.
    public static Dictionary<string, string>? Parse(string[] args);
}
internal sealed record ToolResult(bool Succeeded, string? FileToOpen = null)
{
    public static ToolResult Ok(string? fileToOpen = null);
    public static readonly ToolResult Failed;
}
```

Behavior rules for `ToolInput`:
- **A supplied value always wins.** Matching uses `StringComparer.OrdinalIgnoreCase` on argument names.
- **Not supplied, headless:** print `Headless mode needs <arg>.` and return null.
- **Not supplied, not headless:** open the dialog. A null result from the dialog prints `Action canceled.` and returns null.
- **`Choose`:**
  - Match the value ignoring case and return the option's own spelling.
  - When nothing matches, print `<arg> <value> not found; one of: <options joined by ", ">`. When `fromBuild` is true, append `. If it was just added, build the solution first.`
  - With an empty option list, return null without printing anything; callers print their own "No … found" messages.
- **`Confirm`:** match the value against `trueValue` or `falseValue` ignoring case. Anything else prints `<arg> must be <trueValue> or <falseValue>.`
- **`Name`:** the value must match `^[A-Za-z_][A-Za-z0-9_]*$`, otherwise print `<arg> must be a C# identifier: <value>.`
- **`PositiveInt`:** the value must parse as an int greater than 0, otherwise print `<arg> must be a positive number of milliseconds.`
- **`Text` with an empty value:** treat it as not supplied.

All console messages go to `Console.Out`.

- [ ] **Step 1: Write the failing tests.** Tests use a `FailingDialogs : IDialogPrompt` that calls `Assert.Fail("dialog opened")` in every method, a `StubDialogs` that returns preset values, and a `Capture(Action)` helper that swaps `Console.Out` for a `StringWriter`.

```csharp
[TestMethod] public void Supplied_IsUsedWithoutDialog() =>
    Assert.AreEqual("Idle", Headless(("--state", "Idle")).Text("--state", "p"));
[TestMethod] public void HeadlessMissing_PrintsNeedsAndReturnsNull()
{ var output = Capture(() => Assert.IsNull(Headless().Text("--state", "p"))); StringAssert.Contains(output, "Headless mode needs --state."); }
[TestMethod] public void NotHeadlessMissing_OpensDialog() =>
    Assert.AreEqual("FromDialog", new ToolInput(new Dictionary<string, string>(), false, new StubDialogs(text: "FromDialog")).Text("--state", "p"));
[TestMethod] public void DialogCanceled_PrintsActionCanceled()
{ var output = Capture(() => Assert.IsNull(new ToolInput(new Dictionary<string, string>(), false, new StubDialogs(text: null)).Text("--state", "p"))); StringAssert.Contains(output, "Action canceled."); }
[TestMethod] public void Choose_IgnoresCaseAndReturnsOptionSpelling() =>
    Assert.AreEqual("Running", Headless(("--transition-to", "running")).Choose("--transition-to", ["Idle", "Running"], "p"));
[TestMethod] public void Choose_UnknownValue_ListsOptionsAndBuildHint()
{
    var output = Capture(() => Assert.IsNull(Headless(("--trigger", "Stop")).Choose("--trigger", ["Start", "Poll"], "p", fromBuild: true)));
    StringAssert.Contains(output, "--trigger Stop not found; one of: Start, Poll. If it was just added, build the solution first.");
}
[TestMethod] public void Confirm_MapsValuesAndRejectsOthers()
{
    Assert.IsTrue(Headless(("--transport", "RabbitMQ")).Confirm("--transport", "rabbitmq", "kafka", "p"));
    Assert.IsFalse(Headless(("--transport", "kafka")).Confirm("--transport", "rabbitmq", "kafka", "p"));
    StringAssert.Contains(Capture(() => Assert.IsNull(Headless(("--transport", "mqtt")).Confirm("--transport", "rabbitmq", "kafka", "p"))), "--transport must be rabbitmq or kafka.");
}
[TestMethod] public void Name_RejectsNonIdentifiers() =>
    StringAssert.Contains(Capture(() => Assert.IsNull(Headless(("--state", "Idle State")).Name("--state", "p"))), "--state must be a C# identifier: Idle State.");
[TestMethod] public void PositiveInt_RejectsZeroAndText()
{
    Assert.AreEqual(500, Headless(("--timer-period", "500")).PositiveInt("--timer-period", "p"));
    Assert.IsNull(Headless(("--timer-period", "0")).PositiveInt("--timer-period", "p"));
    Assert.IsNull(Headless(("--timer-period", "fast")).PositiveInt("--timer-period", "p"));
}
[TestMethod] public void WithDefault_UsedOnlyWhenNotSupplied()
{
    Assert.AreEqual("Valve", Headless().WithDefault("--statemachine", "Valve").Text("--statemachine", "p"));
    Assert.AreEqual("Pump", Headless(("--statemachine", "Pump")).WithDefault("--statemachine", "Valve").Text("--statemachine", "p"));
}
```
`ToolArgumentsTest`:
```csharp
[TestMethod] public void Parse_CollectsPairsAndSkipsFlags() =>
    CollectionAssert.AreEquivalent(new Dictionary<string, string> { ["--state"] = "Idle", ["--parent-state"] = "Operating" },
        ToolArguments.Parse(["-a", "-headless", "--state", "Idle", "-r", "x", "--parent-state", "Operating"])!);
[TestMethod] public void Parse_UnknownArgument_Fails() =>
    StringAssert.Contains(Capture(() => Assert.IsNull(ToolArguments.Parse(["--colour", "red"]))), "Unknown argument --colour.");
[TestMethod] public void Parse_MissingValue_Fails() =>
    StringAssert.Contains(Capture(() => Assert.IsNull(ToolArguments.Parse(["-a", "--state"]))), "--state needs a value.");
```
- [ ] **Step 2: Run the tests and check they fail.** Run `dotnet test StatePipes.Test --settings Tests.runsettings --filter "FullyQualifiedName~ToolInputTest|FullyQualifiedName~ToolArgumentsTest"`. Expected: the build fails, because the types don't exist yet.
- [ ] **Step 3: Implement the five files** with the interfaces and rules above. In `ToolArguments.Parse`, a token that starts with `--` is a name and the next token is its value. A missing next token, or a next token that starts with `-`, is a missing value. Other tokens are skipped; `Program` handles them.
- [ ] **Step 4: Run the tests and check they pass.** Same command. Expected: all pass.
- [ ] **Step 5: Commit** with message `feat(tool): add ToolInput, ToolArguments and ToolResult (#95)`.

### Task 2: Program exit codes, and the proxies on ToolInput

**Files:**
- Modify: `Program.cs`, `ProxyGeneratorTool.cs`, `LiveProxyGeneratorTool.cs`, `ProxyGenerator.cs`, `ProxyGeneratorCommon.cs`, `StateMachineTypesHelper.cs`
- Delete: `ProxyInputs.cs`
- Test: `StatePipes.Test/ServiceCreatorTool/ProxyToolInputTest.cs`, replacing `ProxyInputsTest.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces:
  - `Type? StateMachineTypesHelper.GetStateMachineType(IToolInput input)`, which uses one machine without asking, otherwise `input.Choose("--statemachine", names, "Select State Machine:", fromBuild: true)`. `GetStateMachineName(IToolInput input)` returns its `Name`.
  - `ToolResult ProxyGeneratorTool.CreateNewProxy(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, string? updateMonikerFilePath, IToolInput input)`.
  - `ToolResult LiveProxyGeneratorTool.CreateNewLiveProxy(..., int timeoutSeconds, string? updateMonikerFilePath, IToolInput input)`.
  - `internal static string? ProxyGeneratorTool.GetProxyMoniker(string? updateMonikerFilePath, IToolInput input)`.
  - `internal static IToolInput ProxyGeneratorTool.WithStateMachineFromProxyFile(IToolInput input, string? updateMonikerFilePath)`. It returns `input.WithDefault("--statemachine", <short machine name>)`, where the short name is the last `.` segment of the regex match.
  - `ProxyGenerator` and `ProxyGeneratorCommon` constructors take `IToolInput input` in place of `ProxyInputs? inputs`.
  - `Program.Main` is `static int Main(string[] args)`.

Rules:
- The moniker uses `input.Name("--moniker", "Enter the moniker for the proxy")`.
- The DLL uses `input.File("--dll", "Select Class Library To Create Proxy To", "dll files (*.dll)|*.dll|All files (*.*)|*.*", serviceBinDirectory)`.
- The live route's four inputs:
  - `Text("--broker", "Enter Broker URI", <today's default URI>)`
  - `Text("--exchange", "Enter Exchange Name")`
  - `File("--cert", ...)`
  - `File("--certpw", ...)`
- Proxy success is `ToolResult.Ok(newFileOrNull)`, including an update of an existing proxy, which has no file to open. A null input is `ToolResult.Failed`.
- `Program`:
  - parses with `ToolArguments.Parse` and returns 1 when it returns null;
  - builds `new ToolInput(values, _headless, new SelectionDialogPrompt())`;
  - converts `-pi`, `-pri`, `-ii`, `-pti` and `-u` paths with `Path.GetFullPath`;
  - reads `--timeout` from the dictionary;
  - removes the old `--moniker`…`--statemachine` fields.
- Operations not yet converted are wrapped as `ToolResult.Ok(fileName)` (void ones as `ToolResult.Ok()`), so the project builds until Tasks 3–5 convert them.
- `DetermineCreation` returns `ToolResult?`, where null means no argument combination matched.
- `Main` prints `Finished!!!` and returns 0 only when the result succeeded. A null result prints `Incorrect Parameters Specified!` plus today's details and returns 1. A failed result returns 1. An exception prints `Aborting Exception: …` and returns 1.

- [ ] **Step 1: Write the failing tests.** Port each `ProxyInputsTest` case to `ToolInput` with `FailingDialogs`, and add the new ones.
```csharp
[TestMethod] public void GetProxyMoniker_Supplied() => Assert.AreEqual("Peer", ProxyGeneratorTool.GetProxyMoniker(null, Headless(("--moniker", "Peer"))));
[TestMethod] public void GetProxyMoniker_FromUpdateFile() => Assert.AreEqual("Peer", ProxyGeneratorTool.GetProxyMoniker(@"C:\x\Proxies\PeerProxy.cs", Headless()));
[TestMethod] public void GetProxyMoniker_HeadlessMissing_IsNull() => Assert.IsNull(ProxyGeneratorTool.GetProxyMoniker(null, Headless()));
[TestMethod] public void WithStateMachineFromProxyFile_DefaultsToTheShortMachineName()
{
    // proxy file containing "BaseTriggerCommand<Acme.Widgets.StateMachines.Valve.Valve>"
    Assert.AreEqual("Valve", ProxyGeneratorTool.WithStateMachineFromProxyFile(Headless(), proxy).Text("--statemachine", "p"));
    Assert.AreEqual("Pump", ProxyGeneratorTool.WithStateMachineFromProxyFile(Headless(("--statemachine", "Pump")), proxy).Text("--statemachine", "p"));
}
```
  Also add one Program-level test, `Main_HeadlessProxyMissingMoniker_ExitsOneWithoutFinished`. It calls `Program.Main` with `-headless` and the four common arguments pointing at a temp folder generated by `SolutionGeneratorTool`, plus `--dll` naming any existing file, and no `--moniker`. It asserts the return value is `1` and that the output doesn't contain `Finished!!!`. Make `Main` `internal` if needed; `InternalsVisibleTo` is already set.
- [ ] **Step 2: Run the tests and check they fail.** Run `dotnet test StatePipes.Test --settings Tests.runsettings --filter "FullyQualifiedName~ProxyToolInputTest"`. Expected: compile errors.
- [ ] **Step 3: Implement the changes** listed above, and delete `ProxyInputs.cs` and `ProxyInputsTest.cs`.
- [ ] **Step 4: Run the full Unit suite.** Run `dotnet test StatePipes.Test --settings Tests.runsettings --filter TestCategory=Unit`. Expected: all pass. The golden tests are unchanged.
- [ ] **Step 5: Re-run the earlier headless proxy check.** Copy the two-state-machine sample (`new SolutionGeneratorTool` plus `StateMachineGeneratorTool` for `Valve`, then `dotnet build`). Run the built tool with `-proxy -headless … --moniker Fresh --statemachine Valve --dll <Service bin>\Acme.Widgets.dll`. Expected: `Finished!!!`, exit 0, and the solution builds.
- [ ] **Step 6: Commit** with message `feat(tool): exit codes and proxies on ToolInput (#95)`.

### Task 3: State machine, event, trigger and state

**Files:**
- Modify: `StateMachineGeneratorTool.cs`, `EventGeneratorTool.cs`, `TriggerGeneratorTool.cs`, `StateGeneratorTool.cs`, `BaseToolGenerator.cs`, `Program.cs`
- Test: `StatePipes.Test/ServiceCreatorTool/CreateOperationsTest.cs`

**Interfaces:**
- Consumes: `IToolInput`, `ToolResult`, `GetStateMachineName(IToolInput)`.
- Produces. Each method takes `(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, IToolInput input)` and returns `ToolResult`:
  - `CreateNewStateMachine`
  - `CreateNewEvent`
  - `CreateNewTrigger`
  - `CreateNewState`
- Also produces `protected static bool AlreadyExists(string solutionDir, string path)` in `BaseToolGenerator`. When the file exists it prints `<path relative to solutionDir> already exists.` and returns true.

Inputs (the prompts are today's strings):
- State machine: `Name("--statemachine-name", …)`. It refuses if `<projDir>/StateMachines/<Name>/<Name>.cs` exists.
- Event: `Name("--event", …)`. It refuses if `<projDir>/Events/<Event>.cs` exists.
- Trigger:
  - the machine comes from `GetStateMachineName(input)`;
  - the scope comes from `Choose("--trigger-scope", ["public", "internal"], "Select the trigger scope")`, which matches case-insensitively, so dialog users still see lowercase options;
  - the name comes from `Name("--trigger-name", …)`;
  - it refuses if `<projDir>/StateMachines/<M>/Triggers[/Internal]/<Name>.cs` exists.
- State:
  - The machine comes from `GetStateMachineName(input)`.
  - **Parented or not:**
    - When `input.Supplied("--parent-state")`, the state is parented.
    - Otherwise, when `input.Headless`, it is un-parented.
    - Otherwise, the existing type dialog opens through `input.Choose("--state-type", ["Un-parented", "Parented"], "Select the state type")`. `--state-type` is deliberately not in `ToolArguments.Known`, so the dialog always opens.
  - The parent comes from `Choose("--parent-state", stateNames, "Select the parent state", fromBuild: true)`.
  - The name comes from `Name("--state", …)`.
  - It refuses if `<projDir>/StateMachines/<M>/States/<State>.cs` exists.
- `Program` passes `input` and returns their results. It still opens `FileToOpen` when not headless.

- [ ] **Step 1: Write the failing tests** in `CreateOperationsTest`. Its fixture is the same as `ScaffoldOperationGoldenTest`: a generated `Packages.Acme.Widgets` with no DLL. Only operations that don't load the DLL are unit-tested here; trigger and state are covered end to end in Task 7.
```csharp
[TestMethod] public void StateMachine_Headless_CreatesFiles() { Assert.IsTrue(Run(StateMachineGeneratorTool.CreateNewStateMachine, ("--statemachine-name", "Valve")).Succeeded); Assert.IsTrue(File.Exists(Path.Combine(_projectDir, "StateMachines", "Valve", "Valve.cs"))); }
[TestMethod] public void StateMachine_Existing_RefusesAndWritesNothing() { /* create Valve twice; second Succeeded false, output contains "Acme.Widgets\StateMachines\Valve\Valve.cs already exists." , file set unchanged */ }
[TestMethod] public void Event_Headless_CreatesFile() { /* --event StatusEvent → Events/StatusEvent.cs */ }
[TestMethod] public void Event_Missing_FailsWithNeeds() { /* no --event → Succeeded false, "Headless mode needs --event.", no new files */ }
[TestMethod] public void Event_NotIdentifier_Fails() { /* --event "1st" → Succeeded false */ }
```
  `Run(op, params pairs)` calls `op(_solutionDir, SolutionName + ".slnx", Prefix + ".csproj", _targetDir, new ToolInput(pairs dictionary, true, new FailingDialogs()))`. Move `FailingDialogs` and `Capture` into `StatePipes.Test/ServiceCreatorTool/ToolTestHelpers.cs` so Tasks 1–5 share them.
- [ ] **Step 2: Run the tests and check they fail.** Run `dotnet test StatePipes.Test --settings Tests.runsettings --filter "FullyQualifiedName~CreateOperationsTest"`. Expected: compile errors.
- [ ] **Step 3: Implement** the changes above.
- [ ] **Step 4: Run the Unit suite.** Expected: everything passes.
- [ ] **Step 5: Commit** with message `feat(tool): headless state machine, event, trigger and state (#95)`.

### Task 4: Guards and periodic timer

**Files:**
- Modify: `PermitIfGaurdGeneratorTool.cs`, `PermitReentryIfGaurdGeneratorTool.cs`, `IgnoreIfGaurdGeneratorTool.cs`, `PeriodicTriggerGeneratorTool.cs`, `Program.cs`
- Test: no new unit tests, because these load the DLL. Covered in Task 7.

**Interfaces:**
- Produces. Each method takes `(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, string stateFilePath, IToolInput input)` and returns `ToolResult`:
  - `CreatePermitIfGuard`
  - `CreatePermitReentryIfGuard`
  - `CreateIgnoreIfGuard`
  - `CreatePeriodicTrigger`

Inputs:
- PermitIf, PermitReentryIf and IgnoreIf take the trigger from `Choose("--trigger", triggerNames, <today's prompt>, fromBuild: true)`.
- PermitIf also takes `Choose("--transition-to", stateNames, "Select the transition-to state:", fromBuild: true)`.
- Periodic takes `Name("--timer-name", …)` and `PositiveInt("--timer-period", …)`, and passes the period as its string to `GeneratePeriodicTrigger`. It refuses if `Triggers/Internal/<NormalizedName>Trigger.cs` exists.
- An empty trigger or state list keeps today's "No … found" message and returns `Failed`.

- [ ] **Step 1: Implement** the changes above.
- [ ] **Step 2: Run the Unit suite.** Expected: everything passes. The golden tests for the guards and the periodic timer still call the `Generate…` methods directly.
- [ ] **Step 3: Commit** with message `feat(tool): headless guards and periodic timer (#95)`.

### Task 5: New solution and new service

**Files:**
- Modify: `SolutionGeneratorTool.cs`, `ProjectGeneratorTool.cs`, `Program.cs`
- Test: add to `CreateOperationsTest.cs`.

**Interfaces:**
- Produces:
  - `ToolResult SolutionGeneratorTool.CreateNewSolution(IToolInput input)`
  - `ToolResult ProjectGeneratorTool.CreateNewProjects(string solutionDir, string solutionFileName, IToolInput input)`

Inputs:
- New solution:
  - the repo comes from `Folder("--repo", "Repo root directory to place new solution folder")`;
  - the transport comes from `Confirm("--transport", "rabbitmq", "kafka", <today's question>)`;
  - the name comes from `Text("--solution-name", <today's prompt>, "Packages.MyCompany.MyProduct")`.
- New service: `Text("--prefix", <today's prompt>, $"{projectName}.")`.
- Name rules are today's, and their messages are unchanged.
- New solution refuses if the solution folder already exists. New service refuses if `<solutionDir>/<Prefix>` already exists.
- In headless mode, new solution:
  - prints the hosts text from `HostFileReminder` to the console instead of `MessageBox.Show` (split it into `MissingHostEntries()`, which returns the text or null);
  - doesn't open the PDF or launch Visual Studio;
  - prints `Solution created: <full .slnx path>`.
- `Program`'s new-solution branch passes `input`. The branch is chosen exactly as before.

- [ ] **Step 1: Write the failing tests.**
```csharp
[TestMethod] public void NewSolution_Headless_CreatesSolutionWithoutVisualStudio() { /* --repo temp --solution-name Packages.Acme.Gadgets --transport kafka → Succeeded; <temp>/Packages.Acme.Gadgets/Packages.Acme.Gadgets.slnx exists; output contains "Solution created:" */ }
[TestMethod] public void NewSolution_DefaultName_Fails() { /* --solution-name Packages.MyCompany.MyProduct → Succeeded false, "Bad solution name" */ }
[TestMethod] public void NewService_Headless_AddsProject() { /* on the fixture: --prefix Acme.Widgets.Pump → Acme.Widgets.Pump/Acme.Widgets.Pump.csproj exists and the .slnx mentions it */ }
[TestMethod] public void NewService_Existing_Refuses() { /* --prefix Acme.Widgets → "already exists.", Succeeded false */ }
```
- [ ] **Step 2: Run the tests and check they fail.** Expected: compile errors.
- [ ] **Step 3: Implement** the changes above. After this task, `Program` has no `ToolResult.Ok(fileName)` wrappers left.
- [ ] **Step 4: Run the Unit suite.** Expected: everything passes.
- [ ] **Step 5: Commit** with message `feat(tool): headless new solution and new service (#95)`.

### Task 6: Diagrammer headless

**Files:**
- Modify: `StatePipes.Diagrammer/Program.cs`
- Test: `StatePipes.Test/ServiceCreatorTool/DiagrammerArgumentsTest.cs`

**Interfaces (Produces):** the arguments `-headless` and `--out <dir>`. Exit code 0 on success, 1 on an exception. Each PDF prints a `Diagram written: <path>` line, and a successful run ends with `Finished!!!`. If `StateMachinePdfCreator.CreateStateMachineDiagrams` doesn't return the paths, change it to return `List<string>`.

Rules:
- With `-headless`, skip `BuildSolution()` even when `-r` and `-s` are given.
- `--out` replaces `programDataDirectory` and is created if missing.
- A `--` argument other than `--out` prints `Unknown argument <arg>.` and exits 1.

- [ ] **Step 1: Write the failing test.** This is a source-level check, because the Diagrammer uses top-level statements and isn't referenced by the test project. Read `StatePipes.Diagrammer/Program.cs` and assert it parses `-headless` and `--out` (the regex `Equals\("(-headless|--out)"` must match both). The run itself is tested in Task 7.
- [ ] **Step 2: Implement** the changes, and make the top-level program `return 0` or `return 1`.
- [ ] **Step 3: Run the test.** Expected: it passes. Also run `dotnet build StatePipes.Diagrammer` with 0 errors.
- [ ] **Step 4: Commit** with message `feat(diagrammer): headless mode and --out (#95)`.

### Task 7: Headless end-to-end test

**Files:**
- Create: `StatePipes.Test/ServiceCreatorTool/HeadlessEndToEndTest.cs`, with `[TestCategory(TestCategories.Integration)]`
- Modify: `StatePipes.Test/StatePipes.Test.csproj`. Add a `ProjectReference` to `..\StatePipes.Diagrammer\StatePipes.Diagrammer.csproj` so its exe is copied next to the tests.

Helpers:
- `Tool(params string[] args)` runs `StatePipes.ServiceCreatorTool.exe` from `AppContext.BaseDirectory` with the working directory set to the solution folder and a 5-minute timeout. It returns `(int ExitCode, string Output)`.
- `Diagrammer(...)` does the same for the Diagrammer.
- `Build()` runs `dotnet build <slnx> -v q` and asserts exit 0.
- `Common` is `-headless -r "<sln>\" -s Packages.Acme.E2e.slnx -p Acme.E2e.csproj -b "<sln>\Acme.E2e\obj\Debug\net10.0\"`.

- [ ] **Step 1: Write the test** `HeadlessRun_EveryOperation_BuildsAndDiagrams`. It runs these steps in order. After each one it asserts exit 0 and `Finished!!!`, and that the expected file exists.
  1. `-headless --repo <temp> --solution-name Packages.Acme.E2e --transport rabbitmq`
  2. `-headless -r "<sln>\" -s Packages.Acme.E2e.slnx --prefix Acme.E2e`, then `Build()`
  3. `-m Common --statemachine-name Valve`, then `Build()`
  4. `-t Common --statemachine Valve --trigger-name Open --trigger-scope public`
  5. `-t Common --statemachine Valve --trigger-name Tick --trigger-scope internal`
  6. `-a Common --statemachine Valve --state Closed`
  7. `-a Common --statemachine Valve --state Opened`
  8. `-e Common --event ValveOpenedEvent`, then `Build()`
  9. `-a Common --statemachine Valve --state Draining --parent-state Opened`, then `Build()`
  10. `-pi Acme.E2e\StateMachines\Valve\States\Closed.cs Common --trigger Open --transition-to Opened`. This is a relative path: Review Focus 5.
  11. `-pri <Opened.cs> Common --trigger Tick`
  12. `-ii <Closed.cs> Common --trigger Tick`
  13. `-pti <Opened.cs> Common --timer-name Poll --timer-period 500`
  14. A final `Build()` with 0 errors.
  15. `Diagrammer -headless -r "<sln>\" -p Acme.E2e.csproj -c "<sln>\Acme.E2e\obj\Debug\net10.0\" --out <temp>\diagrams`: exit 0, and at least one `.pdf` in `<temp>\diagrams`.
- [ ] **Step 2: Write the test** `HeadlessRun_MissingArgument_FailsWithoutChanges`. On a solution built like steps 1–3, for each operation run it without one required argument:
  - `-m` without `--statemachine-name`
  - `-a` without `--state`
  - `-t` without `--trigger-name`
  - `-e` without `--event`
  - `-pi` without `--transition-to`
  - `-pri` without `--trigger`
  - `-ii` without `--trigger`
  - `-pti` without `--timer-period`
  - new service without `--prefix`

  Assert exit 1, `Headless mode needs`, no `Finished!!!`, and that the set of files and their last-write times is unchanged.
- [ ] **Step 3: Write the test** `HeadlessRun_ExistingState_Refuses`. Run `-a … --state Closed` a second time. Expected: exit 1, `already exists.`, and `Closed.cs` unchanged.
- [ ] **Step 4: Run them.** Run `dotnet test StatePipes.Test --settings Tests.runsettings --filter "FullyQualifiedName~HeadlessEndToEndTest"`. Expected: all pass. Fix the tool, not the test, when a step fails for a reason the spec doesn't allow.
- [ ] **Step 5: Commit** with message `test(tool): headless end-to-end run of every operation (#95)`.

### Task 8: Plugin recipes and the argument-coverage test

**Files:**
- Modify: `StatePipes.ClaudePlugin/skills/statepipes-scaffold/SKILL.md`, and every file in `StatePipes.ClaudePlugin/skills/statepipes-scaffold/operations/*.md`
- Modify: `StatePipes.Test/ClaudePlugin/PluginGuidanceTest.cs`

Content:
- **SKILL.md intro:** every operation runs the installed tool headless, and hand-building is only a fallback.
- **"Running the tool headless" in SKILL.md:**
  - Gives `<Tool>` and `<Diagrammer>` (`${env:ProgramFiles(x86)}\StatePipes\Diagrammer\StatePipes.Diagrammer.exe`, from the installer's `$INSTDIR\Diagrammer`) and the `Common` arguments.
  - Says "run `dotnet build <Solution>.slnx` before any operation that reads the built DLL: add-state, add-trigger, guards, add-periodic, proxies, diagram".
  - Says success means `Finished!!!` and exit 0.
  - Says that on `Unknown argument`, or a failure without `Headless mode needs` from a tool older than this release, Claude uses the recipe's Fallback section.
- **Each recipe:**
  - Gets a `## Run the tool` section with one fenced PowerShell command that uses the Task 7 argument shapes.
  - The existing hand-built `## Steps` section is renamed `## Fallback (tool without headless support)`.
  - The `Template:`, `Creates:` and `Marker:` lines are unchanged.
  - `new-solution` and `new-service` keep their CLAUDE.md steps after the tool run.
  - `diagram` runs the Diagrammer with `--out "<SolutionDir>\<Prefix>\Diagrams"`, then reads the PDFs.
  - The Add Event paragraph in SKILL.md gets its own command.

- [ ] **Step 1: Write the failing test.** Replace `ProxyRecipes_RunTheToolHeadlessWithArgumentsItParses` with `Recipes_RunTheToolHeadlessWithArgumentsItParses`. It reads every recipe and SKILL.md and collects the `--name` tokens in fenced code blocks, the same way the current test does. It asserts:
  - every operation recipe except `update-proxy` has a `## Run the tool` section containing `-headless`;
  - every collected `--` argument is in `ToolArguments.Known` or is `--out`;
  - each of `--statemachine-name`, `--state`, `--trigger-name`, `--trigger-scope`, `--event`, `--trigger`, `--transition-to`, `--timer-name`, `--timer-period`, `--prefix`, `--solution-name`, `--repo`, `--transport` and `--out` appears at least once.
  Keep the existing assertions for the proxy arguments.
- [ ] **Step 2: Run it and check it fails.** Expected: it fails, because the recipes have no `Run the tool` section yet.
- [ ] **Step 3: Edit the recipes and SKILL.md.**
- [ ] **Step 4: Run the Unit suite.** Expected: everything passes, including `RecipeTemplateCheckTest` and the golden tests, which read the unchanged Template, Creates and Marker lines.
- [ ] **Step 5: Validate the plugin.** Run `claude plugin validate StatePipes.ClaudePlugin`. Expected: `Validation passed`.
- [ ] **Step 6: Commit** with message `feat(plugin): scaffold recipes run the tools headless (#95)`.

### Task 9: Docs and final verification

**Files:**
- Modify: `docs/VisualStudioExternalTools.md`. Rename `## Headless Proxy Generation` to `## Headless Mode`, with one argument table per operation (the spec's table), PowerShell examples for both executables, and the success and exit-code rule.
- Modify: `StatePipes.Test/ClaudePlugin/PluginGuidanceTest.cs`. Extend the argument-coverage source list with the docs' Headless Mode code blocks.

- [ ] **Step 1: Write the docs** and extend the test.
- [ ] **Step 2: Run the full test suite** with Integration included: `dotnet test StatePipes.Test --settings Tests.runsettings`. Expected: everything passes.
- [ ] **Step 3: Format the changed files.** Run `dotnet format StatePipes.slnx --include <changed .cs files> --verify-no-changes`. Expected: exit 0.
- [ ] **Step 4: Manual check in Visual Studio.** Ask the user to run **Add State** and **Add Trigger** from the External Tools menu on a sample service and confirm the dialogs appear as before. This is a step for the user, not one to automate.
- [ ] **Step 5: Commit** with message `docs: headless mode for every tool (#95)`.
