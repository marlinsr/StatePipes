# StatePipes Project Memory

## Technology Stack
- **Runtime & Language:** .NET / C# (Modern Async/Await patterns)
- **Architecture:** Event-Driven State Machine Engine
- **Core Components:** CommandBus (Triggers/Commands), EventBus (Pub/Sub Events), StatePipesProxy (External Comms)

## Dev & Test Commands
- **Build Solution:** `dotnet build`
- **Run Tests:** `dotnet test --settings tests.runsettings`
- **Run Single Test File:** `dotnet test --filter FullyQualifiedNameOfTest`
- **Code Linting/Formatting:** `dotnet format`

## Code Conventions & Standards
- **Asynchronous Execution:** Always use modern `async/await`. Long-running or fire-and-forget operations must wrap execution boundaries safely using `TaskWrapper`.
- **State Modifications:** Encapsulate configuration lifecycles strictly. All state adjustments or hooks must execute through `OnEntry` and `OnExit` life cycle methods.
- **Service Registration:** Manage inter-service topology and remote node bindings using `ServiceConfiguration` and its `ProxyConfigurations` array.
- **Messaging Flow:** 
  - Direct execution prompts use `SendCommand` via the internal `CommandBus`.
  - System-wide updates and broadcasts use `PublishEvent` via the `EventBus`.
  - Process-wide sync utilizes `SendCurrentStatusAllStateMachines()` pushing an `AllStateStatusEvent`.

## Behavioral Rules
- **Rule 1:** Never write synchronous blocking calls (`.Result` or `.Wait()`) within state machine nodes or asynchronous loops.
- **Rule 2:** Always verify state machine updates against the baseline criteria outlined in `docs/Paper.md` to protect architectural integrity.
- **Rule 3:** Always run `dotnet format` after creating or changing codebase modules before completing the task.
