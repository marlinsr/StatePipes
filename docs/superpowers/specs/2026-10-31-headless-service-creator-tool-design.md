# Headless ServiceCreatorTool and Diagrammer — design

Issue: #95. Sub-project 1 of the Claude Code plugin work (#93, #94).

## Goal

Every ServiceCreatorTool operation, and the StateMachine Diagrammer, runs with `-headless`: no Visual Studio, no
dialogs, every input given as a command-line argument. Claude (through the `statepipes` plugin), scripts and CI then
run the real generators instead of re-implementing them from the templates. Without `-headless`, the Visual Studio
External Tools behave as they do today.

## Decisions

- One named argument per input (for example `--state`, `--parent-state`, `--trigger-name`), not shared generic
  names.
- Every input goes through one interface, `IToolInput`. Its single implementation uses an argument when one is
  given, reports an error in headless mode, and otherwise opens today's dialog. The proxy tools move onto it and
  `ProxyInputs` is removed.
- The Diagrammer gets `-headless` and `--out <dir>`.
- A run succeeds only when it prints `Finished!!!` and exits 0.
- What each generator writes does not change.

## IToolInput

```csharp
internal interface IToolInput
{
    bool Headless { get; }
    string? Text(string arg, string prompt);                                    // GetUserInput / ShowInputDialog
    string? Choose(string arg, IReadOnlyList<string> options, string prompt);   // ShowListSelection
    string? File(string arg, string prompt, string filter, string? initialDirectory = null); // SelectFile
    string? Folder(string arg, string prompt);                                  // FolderBrowserDialog
    bool? Confirm(string arg, string trueValue, string falseValue, string prompt); // GetUserConfirmation
}
```

`ToolInput(IReadOnlyDictionary<string, string> args, bool headless, IDialogPrompt dialogs)` implements it. For each
call:

1. If `args` has a value for `arg`, use it. This holds without `-headless` too, as `--moniker` does today.
2. Otherwise, in headless mode, print `Headless mode needs <arg>.` and return null.
3. Otherwise open the dialog through `IDialogPrompt`. A canceled dialog returns null.

Validation:

- `Choose` matches the value against `options` ignoring case and returns the option's own spelling. A value that
  isn't one of them prints `<arg> <value> not found; one of: A, B, C` and returns null. An empty option list prints
  the generator's existing "No ... found" message.
- `Confirm` maps `trueValue` / `falseValue` ignoring case. Any other value prints
  `<arg> must be <trueValue> or <falseValue>` and returns null.
- `Text` returns a supplied value unchanged. Generators keep their own rules (for example the solution and prefix
  name checks).

`IDialogPrompt` has one method per dialog kind. `SelectionDialogPrompt` forwards to `SelectionDialog` and the WinForms
folder dialog. Tests use a fake whose methods fail the test, so a headless test proves no dialog opened.

Each `Create…` method takes an `IToolInput` and calls it wherever it calls `SelectionDialog` today. A null result
stops the operation before its `Generate…` method runs, so no file is written. This is how a canceled dialog already
behaves.

## Arguments

Existing arguments (`-r`, `-s`, `-p`, `-b`, the operation flags, `-u`, `-live`, `--timeout`, `-headless`) are
unchanged. `ParseArgs` collects every `--name value` pair into one dictionary that `ToolInput` reads. `--timeout`
stays a parsed integer.

| Operation (flag) | Input | Argument | Dialog today |
|---|---|---|---|
| New solution (no `-r`/`-s`/`-p`) | repo folder | `--repo <dir>` | folder picker |
| | transport | `--transport rabbitmq\|kafka` | "Are you using RabbitMQ?" yes/no |
| | solution directory name | `--solution-name Packages.X.Y` | text |
| New service (`-r -s`, no `-p`) | project prefix | `--prefix X.Y.Svc` | text |
| Add state machine (`-m`) | machine name | `--statemachine-name` | text |
| Add state (`-a`) | machine | `--statemachine` | list (only with more than one machine) |
| | parent | `--parent-state` | state type list, then parent list |
| | state name | `--state` | text |
| Add trigger (`-t`) | machine | `--statemachine` | list (only with more than one machine) |
| | scope | `--trigger-scope public\|internal` | list |
| | trigger name | `--trigger-name` | text |
| Add event (`-e`) | event name | `--event` | text |
| PermitIf (`-pi <state file>`) | trigger | `--trigger` | list of the machine's triggers |
| | target state | `--transition-to` | list of the machine's states |
| PermitReentryIf (`-pri <state file>`) | trigger | `--trigger` | list |
| IgnoreIf (`-ii <state file>`) | trigger | `--trigger` | list |
| Periodic (`-pti <state file>`) | timer name | `--timer-name` | text |
| | period | `--timer-period <ms>` | text |
| Proxy, update proxy, live proxy | unchanged | `--moniker`, `--dll`, `--statemachine`, `--broker`, `--exchange`, `--cert`, `--certpw` | unchanged |

Rules:

- **Add state:** a given `--parent-state` means a parented state. In headless mode, no `--parent-state` means an
  un-parented state, so headless mode has no state-type argument. Without `-headless` and without `--parent-state`
  the state-type dialog opens as today. First-child detection is unchanged.
- **Machine selection** (`--statemachine`) keeps today's rule: one machine is used without asking. More than one
  needs the argument in headless mode, or the dialog otherwise. It moves from `StateMachineTypesHelper` onto
  `IToolInput.Choose`.
- **`--timer-period`** must be a positive integer. Otherwise the tool prints `--timer-period must be a positive number
  of milliseconds` and stops.
- **Live proxy:** the four connection arguments are read through `IToolInput`. A partial set in headless mode names
  the missing arguments.
- **Update proxy (`-u`):** still keeps the machine that the existing proxy targets when `--statemachine` isn't given.

## Program flow (ServiceCreatorTool)

- The argument combination that picks an operation is unchanged.
- With `-headless`, no operation attaches to Visual Studio, builds, or opens files. Headless callers `dotnet build`
  first for any operation that reads the built DLL: state, trigger, guards, periodic and proxies.
- New solution in headless mode does not show the hosts-file `MessageBox`, open the PDF, or launch Visual Studio. It
  prints the hosts reminder to the console when entries are missing, then prints the new solution's path.
- Each `Create…` method returns `ToolResult(bool Succeeded, string? FileToOpen)`. `Program` opens `FileToOpen` in
  Visual Studio when not headless, as today.
- `Main` returns an exit code. On success it prints `Finished!!!` and returns 0. On failure it prints the reason (a
  canceled dialog prints `Action canceled.`) and returns 1, and `Finished!!!` is not printed. Unknown argument
  combinations still print `Incorrect Parameters Specified!` and return 1. Exceptions still print
  `Aborting Exception: ...` and return 1.

## Diagrammer

- `-headless` skips the Visual Studio build.
- The `-c` path is still repointed to `<Prefix>.Service/bin/<config>` when `-r` and `-p` are given. A headless caller
  passes the same values as for ServiceCreatorTool, with `-c` set to the class library's `obj\<config>\<tfm>\` folder.
- `--out <dir>` sets the PDF folder; it is created if missing. The default stays
  `%ProgramData%\StatePipes\StatePipes.Diagrammer`.
- It prints the path of each PDF and ends with `Finished!!!`, exit code 0. An exception prints its message and exits
  with code 1.

## Plugin (`statepipes-scaffold`)

- `SKILL.md`: the intro says every operation runs the installed tool headless. "Running the tool headless" covers
  both executables, the common arguments, the build-first rule, and success meaning `Finished!!!` plus exit code 0.
- Each recipe in `operations/` gets a "Run the tool" step with its exact command. The current hand-built steps stay
  under "Fallback". They are used only when the installed tool doesn't support headless mode for that operation:
  it rejects an argument, or exits without `Finished!!!` and doesn't print `Headless mode needs`. The `Template:`,
  `Creates:` and `Marker:` lines stay, so the golden tests keep checking them against the generators.
- `new-solution` runs the tool, then writes CLAUDE.md as now. `new-service` runs the tool, then adds the Services row.
- `diagram` runs the Diagrammer headless with `--out "<SolutionDir>\<Prefix>\Diagrams"`, then reads the PDFs and
  compares them with the approved model.
- `statepipes-verify` is unchanged.

## Docs

`docs/VisualStudioExternalTools.md`: "Headless Proxy Generation" becomes "Headless Mode", with an argument table per
operation, a PowerShell example for each executable, and the success rule.

## Testing

All tests are MSTest in `StatePipes.Test`.

- **`ToolInputTest`** (Unit). Covers:
  - a supplied argument is used and no dialog opens;
  - a missing value prints `Headless mode needs <arg>.` and returns null in headless mode;
  - without headless, a missing value goes to the dialog;
  - `Choose` ignores case, returns the canonical spelling, and rejects values that aren't options;
  - `Confirm` mapping;
  - `--timer-period` validation.
- **Argument coverage** (Unit). Every `--` argument in the docs' headless section and in the scaffold recipes'
  command blocks is one that `ParseArgs` accepts, for both executables. This replaces
  `ProxyRecipes_RunTheToolHeadlessWithArgumentsItParses`.
- **Proxy tests:** `ProxyInputsTest` becomes tests of the same rules through `ToolInput`.
- **Golden tests:** unchanged.
- **Headless end-to-end** (Integration, on a temp folder, using the built tools). The sequence:
  1. new solution, then new service, then `dotnet build`;
  2. add a state machine, a state (un-parented and parented), a public and an internal trigger, an event, a PermitIf,
     a PermitReentryIf, an IgnoreIf and a periodic timer, building whenever the next step reads the DLL;
  3. a final build with 0 errors;
  4. the Diagrammer headless with `--out`, which writes at least one PDF.

  Every step prints `Finished!!!` and exits 0. For each operation, a run missing one required argument exits 1,
  prints `Headless mode needs`, and leaves the folder's files unchanged.
- **Visual Studio paths:** checked manually once, by running Add State and Add Trigger from the External Tools menu.
  These paths have no automated test.

## Out of scope

- The Installer shipping the plugin (sub-project 3).
- Multi-service design guidance.
- Changing generated file contents or templates.
