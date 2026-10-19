# OPC UA Bridge Exec — Design

**Date:** 2026-10-19
**Branch:** `opcua-bridge`
**Component:** `StatePipes.OpcUaBridge` (plus test methods in `OpcUaServer`)

## Goal

Let StatePipes clients call OPC UA methods through the bridge. For every method the session may execute, the
bridge exposes an Exec command carrying the method's input arguments and an ExecResult event that returns the
method's output arguments, or why the call failed.

Because order matters to the clients, the bridge also changes how it serves commands: Get, Set, Exec, Monitor
and Delete commands go through one queue and take effect, and are answered, in the order the bridge received
them. That replaces today's separate Get loop and `SetWriter`.

## Requirements (as agreed)

1. Every executable method gets a runtime Exec command whose body holds one property per input argument.
2. On receipt the bridge calls the method with those arguments and answers the sender with the method's
   ExecResult event, holding the output arguments on success and the status in every case.
3. ExecResult is a response to the sender only, guarded by `ReplyToGuard`; it is never published on the event
   exchange.
4. A method is executable when `Executable` and `UserExecutable` are true; an optional `OPCUA_METHOD_FILTER`
   regex narrows it further. Methods in namespace 0 are never exposed.
5. A call is made on an (ObjectId, MethodId) pair. A method node reached from several objects gets one Exec per
   object.
6. Clients correlate an ExecResult with its Exec by order. There is no correlation id.
7. Get, Set, Exec, Monitor and Delete commands are served one at a time, bridge-wide, in arrival order, and are
   answered in that order, refusals included.
8. The bridge never times out a call; it waits as long as the server takes.
9. `OpcUaServer` gains test methods covering each argument shape and each rule above.

## Non-goals

- A correlation id on Exec / ExecResult (chosen against: clients rely on order).
- Concurrent calls. One command at a time, bridge-wide.
- Method calls with an input argument Set cannot encode: matrices, unfixed rank, abstract types (Number,
  Integer, UInteger), string-fallback types (NodeId, LocalizedText, QualifiedName, undecodable structures, ...),
  structures without a binary encoding.
- Methods on ObjectTypes, or methods reached from anything other than an Object.
- Retrying failed calls, or holding calls until an OPC UA session returns.
- An audit trail of who called what. That belongs to the planned authorization work.
- Ordering monitor notifications against commands. Notifications are not queued.

## Message contracts

### Per-method messages

Named like the item messages, from the method's NodeId. For `ns=2;s=CalculatePressureVolumeMass` on exchange
`Line1Plc`:

| Message | Full name |
|---|---|
| Exec command | `Line1Plc.Exec_ns2_s_CalculatePressureVolumeMass_Command` |
| ExecResult event | `Line1Plc.ExecResult_ns2_s_CalculatePressureVolumeMass_Event` |

When one method node is reached from more than one object, **every** pair sharing it is named with the object
first, then the method, separated by a double underscore: method `ns=2;s=Start` on objects `ns=2;s=Pump1` and
`ns=2;s=Pump2` gives `Exec_ns2_s_Pump1__ns2_s_Start` and `Exec_ns2_s_Pump2__ns2_s_Start`. No pair keeps the bare
name.

Names are fitted to 255 bytes with the existing hash and made unique with the existing collision suffix by
running `OpcUaNaming.AssignUniqueNames` over all methods. `Exec_` names cannot collide with `Get_` names.
`ExecResult_..._Event` is shorter than `MonitorStatus_..._Event`, so `LongestNameExtra` is unchanged.

`MessageNames` gains `ExecPrefix = "Exec_"`, `ExecResultPrefix = "ExecResult_"`, the matching name properties
and `FromExecCommandFullName`, kept apart from `FromCommandFullName` and `FromSetCommandFullName` as Set's is.

### Argument property names

Each `Argument.Name` becomes a property name by `OpcUaNaming.Sanitize`. An empty name becomes `Arg{index}`
(zero-based position), a name starting with a digit gets a `_` prefix, and duplicates get `_2`, `_3`, ... in
argument order. In the ExecResult, an output whose name would be `ObjectId`, `MethodId`, `StatusCode` or
`Status` gets the next free suffix.

### Exec command

| Property | Type | Notes |
|---|---|---|
| one per input argument, in argument order | typed as a Set `Value` of the argument's DataType and ValueRank | Not nullable. A method with no inputs has an empty command. |

### ExecResult event

| Property | Type | Notes |
|---|---|---|
| `ObjectId` | string | The object called on, in standard notation (`ns=2;s=PLC_Data`); empty for a refusal of a method the catalog does not hold. |
| `MethodId` | string | The method called (`ns=2;s=CalculatePressureVolumeMass`); empty as for `ObjectId`. |
| `StatusCode` | uint | The Call's result, or the bridge's reason for refusing. |
| `Status` | string | `StatusCode` as text. For `BadInvalidArgument` it also names each input the server rejected and its code, e.g. `BadInvalidArgument: percent (BadOutOfRange)`. |
| one per output argument, in argument order | typed as a Get event's `Value` of the argument's DataType and ValueRank | Present only when `StatusCode` is not Bad; otherwise omitted, never null. |

### Responses

ExecResult goes to the sender's `ResponseExchangeName` on the main connection channel, like a SetStatus. Every
Exec that passes the guard gets exactly one ExecResult, unless the bridge shuts down during its call.

### Ordering

All commands except `GetSelfDescription` are served in the order the bridge received them, and their answers
are sent in that order. See Serving commands.

### Self-description size

Each kept method adds two types. A test measures bytes per method, which goes in the README. The existing size
warning in `OpcUaBridgeService.SetCatalog` covers the limit; `OPCUA_NODE_FILTER` does not apply to methods,
`OPCUA_METHOD_FILTER` does.

## Components

### `CommandExecutor` (new)

Replaces `SetWriter` and `OpcUaBridgeService.ServeGetsAsync`. Owns the single command queue, the batching of
runs, and the answers.

- An unbounded single-reader `Channel<PendingCommand>`. `PendingCommand` is a Get, Set, Exec, Monitor, Delete,
  DeleteUnlisted or Refusal, each carrying the reply-to (`BusConfig` and raw bytes) and, where needed, a copy of
  the body.
- Enqueue methods are called by `OpcUaBridgeService.ConsumeCommand` on the transport's receive thread and only
  write to the channel.
- `RunAsync(CancellationToken)`: the single reader (see Serving commands).
- `Complete()`: completes the channel on dispose.
- Depends on three seams, each faked in `CommandExecutorTest`:
  - `IOpcUaSession`: `ReadValuesAsync`, `WriteValuesAsync`, `CallAsync`. Implemented by `OpcUaClient`.
  - `ICommandOutput`: sends a Get event, a SetStatus, an ExecResult, and a MonitorStatus refusal. Implemented by
    `OpcUaBridgeService`.
  - `IMonitorCommands`: `MonitorAsync`, `DeleteAsync`, `DeleteUnlistedAsync`. Implemented by
    `MonitorCoordinator`.

### `OpcUaMethodItem` (new record)

`ObjectId`, `MethodId`, `ObjectIdText`, `MethodIdText`, `Names` (`MessageNames`), `Inputs` and `Outputs`
(lists of `MethodArgument`). `ExecCommandTypeFullName` and `ExecResultEventTypeFullName`.

`MethodArgument`: `PropertyName` (sanitized, unique), `BuiltInType`, `ValueType` (CLR type, as an item's),
`Structure` (`StructureLayout?`), `IsArray`.

### `ExecArgumentEncoder` (new, static)

Turns an Exec body into the call's input `Variant`s, one per input in order, using `OpcUaValueEncoder` or
`StructureEncoder` per argument with Set's rules. Returns `BadArgumentsMissing`, `BadTooManyArguments`,
`BadTypeMismatch` or `BadOutOfRange` as under Error handling.

### Changes to existing components

- **`BridgeSettings`:** `MethodFilter` (`Regex?`) from `OPCUA_METHOD_FILTER`, built like `WriteFilter`.
- **`OpcUaClient`:**
  - `DiscoverAsync` returns the methods as well as the data items. The browse mask adds `NodeClass.Method`; a
    method's parent is recorded when the parent is an Object.
  - Reads `Executable`, `UserExecutable` and the `InputArguments` / `OutputArguments` property values, and builds
    structure layouts for structure argument types with `StructureLayoutBuilder`.
  - `CallAsync(NodeId objectId, NodeId methodId, IReadOnlyList<Variant> inputs, CancellationToken)` returns the
    `CallMethodResult`, or a result with `BadNotConnected` when there is no connected session.
  - **Call session (revised 2026-10-19):** the client stack cuts every request off at the session's
    `OperationTimeout` (15 s) and ignores `RequestHeader.TimeoutHint`, so Calls go over a **second OPC UA
    session** whose `OperationTimeout` is `int.MaxValue`. It is opened on the first Call after each connect
    (servers whose methods are never called see one session), reopened if it has failed, and closed by
    `DisconnectAsync` together with the main session. Reads, Writes, keep-alives and monitors stay on the main
    session with their 15 s limit, so a dead connection is still detected there; losing the main session closes
    the call session, which ends a running call as outcome unknown.
- **`MonitorCoordinator`:** `Monitor`, `Delete` and `DeleteUnlisted` become `MonitorAsync`, `DeleteAsync` and
  `DeleteUnlistedAsync`, which return a `Task` that completes once the request has been handled (registry
  updated, applied to the live session if any, MonitorStatus sent). The request still goes through the
  coordinator's own queue, so it never races session start or loss, reply-to checks or expiry, which keep
  arriving directly.
- **`OpcUaBridgeService`:** the catalog gains `ByExecCommand`. `ConsumeCommand` turns every command except
  `GetSelfDescription` into a `PendingCommand` and enqueues it, refusals included. `RunAsync` runs
  `CommandExecutor.RunAsync` in place of `ServeGetsAsync` and `SetWriter.RunAsync`. It implements
  `ICommandOutput`. Its discovery log line also counts methods.
- **`SelfDescriptionBuilder`:** adds the Exec command and ExecResult event for each kept method.
- **`MessageNames` / `OpcUaNaming`:** as under Per-method messages.
- **Removed:** `SetWriter`, `ISetSession`, `ISetOutput`.

## Data flow

### Discovery

The existing browse from `ObjectsFolder` also returns Method nodes. For each Method reached from an Object, the
(ObjectId, MethodId) pair is a candidate. Methods are not browsed into, except for their argument properties.
A Method reached from anything other than an Object is ignored.

A candidate is kept when all hold:

1. The MethodId is not in namespace 0.
2. `Executable` and `UserExecutable` are both true.
3. The MethodId text matches `MethodFilter`, or `MethodFilter` is unset.
4. Every input argument has a shape Set can encode: a natural CLR type (`OpcUaValueMapper.MapScalar` non-null),
   ValueRank `Scalar` or `OneDimension`, not abstract; or a decodable structure, scalar or 1-D, whose layout and
   every nested layout has a `BinaryEncodingId`.

A method without an `InputArguments` or `OutputArguments` property has no inputs or outputs. Outputs follow
Get's rules: an output with no natural form is published as its string fallback, so an output never hides a
method.

Candidates failing 1–3 are left out silently. Those failing 4 are counted by reason (matrix or unfixed rank,
abstract type, string fallback, no binary encoding), and discovery logs one Info summary, e.g.
`2 executable methods have no Exec command (matrix: 1, string fallback: 1)`, with each method and reason at
Verbose.

### Commands

`ConsumeCommand`, on the receive thread:

1. `GetSelfDescription` is answered at once from the catalog, as today.
2. Get: `TryGetReplyTo` (no `ReplyToGuard`, as today), then enqueue. An unknown Get is still ignored.
3. Set, Exec, Monitor, Delete: `TryGetAllowedReplyTo`; on failure the command is dropped unanswered and
   logged.
4. A command the catalog holds is enqueued as itself, with a copy of its body (the transport may reuse its
   buffers).
5. A Set, Exec, Monitor or Delete the catalog does not hold, recognised by its `MessageNames.From...` method, is
   enqueued as a Refusal with the status the existing rules give (`BadWaitingForInitialData`,
   `BadNodeIdUnknown`, `BadNotWritable`, `BadMethodInvalid`), or as a DeleteUnlisted.
6. A Monitor whose `MonitorParameters.Parse` fails is enqueued as a Refusal with the parse status.

### Serving commands

The single reader takes the longest run of one kind from the head of the queue and serves it as one unit:

| Run | Served as | Cut when |
|---|---|---|
| Gets | one `ReadValuesAsync` (≤ 500) | the next command is not a Get, or the batch is full |
| Sets | one `WriteValuesAsync` (≤ 500), Set encoding as today | the next command is not a Set, a node repeats, or the batch is full |
| Exec | one `CallAsync` | always a run of one |
| Monitor / Delete / DeleteUnlisted | the `IMonitorCommands` call, awaited | always a run of one |
| Refusal | its answer sent | always a run of one |

The next run starts only after every answer of the current run has been sent, so effects and answers both
follow arrival order: Get(A), Set(A), Get(A) is three runs and reads the old value, writes, then reads the new
one.

An Exec is served as:

1. Encode the inputs with `ExecArgumentEncoder`. A failure is answered at once and nothing is called.
2. `CallAsync`, waiting as long as the server takes.
3. Build the ExecResult: outputs decoded as Get values when the status is not Bad; for `BadInvalidArgument`,
   the rejected inputs named in `Status` from `InputArgumentResults`.
4. Send it.

No command is ever retried.

## Error handling

### Exec

| Situation | Behaviour | ExecResult `StatusCode` |
|---|---|---|
| Reply-to fails `ReplyToGuard` | Dropped and logged, not called | none sent |
| Exec before the first discovery | Refusal, answered at its turn | `BadWaitingForInitialData` |
| Exec for a method the catalog does not hold (gone, filtered, not executable, unencodable input) | Refusal, answered at its turn | `BadMethodInvalid` |
| Body not a JSON object, or an input property missing | Refused before the Call | `BadArgumentsMissing` |
| A property that is not an input | Refused before the Call | `BadTooManyArguments` |
| Wrong JSON kind, missing value-type structure field, unknown structure field | Refused before the Call | `BadTypeMismatch` |
| Number outside the type's range | Refused before the Call | `BadOutOfRange` |
| No connected OPC UA session | Fails fast, no waiting for reconnect | `BadNotConnected` |
| Call throws, or the session drops during the call | Logged as an error; **outcome unknown**; the next command runs | the exception's `StatusCode`, else `BadCommunicationError` |
| Server returns Bad | Passed through; for `BadInvalidArgument` the rejected inputs are named in `Status` | the server's code |
| Server returns Good or Uncertain | Outputs included | the server's code |
| Rediscovery while queued | A queued Exec keeps its method item and calls by NodeId | the server's result |
| Bridge shuts down during a call | The call is cancelled | none sent |
| Broker down when ExecResult is sent | Logged and lost, as a Get event is today | none sent |

`BadTimeout`, `BadCommunicationError` and `BadNotConnected` after a call started mean **the outcome is unknown**:
the method may have run. Because answers are matched by order, a lost ExecResult (broker down) also leaves that
client's matching out of step; the README says so.

**No timeout:** Calls use the dedicated call session (see `OpcUaClient`). A method that never returns holds the queue, and every Get, Set, Exec, Monitor and Delete behind
it, until it returns or the session drops. Notifications from existing monitors keep flowing.

**Logging:** failed Execs at Warning (object, method, sender `CommandExchangeName`, status); successful ones at
Verbose.

### Get, Set, Monitor and Delete after the refactor

Only when each command is served changes. What each answers is unchanged:

- Get keeps `TryGetReplyTo`, Read batching, its error handling and event body.
- Set keeps every SetStatus code, the encoding rules and the node-repeat batch cut. Its refusals are queued
  instead of answered at once.
- Monitor and Delete keep their MonitorStatus answers and `DeleteUnlisted`. Parse failures and unknown-node
  refusals are queued.
- A Monitor with no OPC UA session still resolves at once as `Pending`, so it does not stall the queue.

Interleaved Gets and Sets lose some batching: Get, Set, Get, Set is four round trips instead of two.

## `OpcUaServer` test methods

A new `PLC_Data/Methods` object, beside the existing `CalculatePressureVolumeMass` (unchanged):

| Method (`ns=2;s=Methods.…`) | Signature / behaviour | Expected bridge result |
|---|---|---|
| `Add` | (Int32 a, Int32 b) → Int32 sum | Exec |
| `Echo` | (Double, Int16, Boolean, String, DateTime, Double[], Byte[]) → the same values | Exec, every natural type round trips |
| `EchoDensity` | (DensityDataType, DensityDataType[]) → the same values | Exec, structures |
| `NoArgs` | () → (); increments `Methods.NoArgsCount` (a readable Int32 variable) | Exec with empty command and result |
| `Slow` | (UInt32 ms) → (); waits `ms` | Exec; no timeout at 20 000 ms; queue ordering |
| `Reject` | (Double percent) → (); `BadInvalidArgument` with input `BadOutOfRange` outside 0–100 | `percent` named in `Status` |
| `TextOut` | () → LocalizedText | Exec; output as string fallback |
| `NotExecutable` | `Executable` false | no Exec |
| `UserNotExecutable` | `Executable` true, `UserExecutable` false | no Exec |
| `MatrixIn` | (Double, ValueRank 2) → () | no Exec; counted in the summary |
| `Start` | one method node referenced from objects `Methods.Pump1` and `Methods.Pump2` | two object-qualified Execs |

`PlcServerFixture` exposes these NodeIds.

## Testing

### Unit tests (`StatePipes.Test/OpcUaBridge`)

- Naming: Exec and ExecResult names; the object-qualified form for a shared method; collision suffix; longest
  name within 255 bytes; `FromExecCommandFullName`.
- Argument naming: empty name → `Arg{n}`; leading digit; duplicates; reserved output names.
- `ExecArgumentEncoderTest`: each input shape; missing argument → `BadArgumentsMissing`; extra property →
  `BadTooManyArguments`; wrong kind → `BadTypeMismatch`; out of range → `BadOutOfRange`; empty body for a method
  with no inputs.
- `CommandExecutorTest` (fake session, output and monitor commands): every `SetWriterTest` case, moved; plus:
  - Get, Set, Get is three runs in order.
  - Consecutive Gets are one Read; consecutive Sets one Write.
  - An Exec always runs alone.
  - A Monitor waits for the run before it, and the next run waits for its task.
  - Refusals are answered in queue position.
  - A slow Exec holds back later commands.
  - A Call exception gives the unknown-outcome code, then the queue continues.
  - Cancellation during a call sends no answer.
- `SelfDescriptionBuilder`: Exec and ExecResult only for kept methods; the Explorer-consumption test extended to
  them; a size test recording bytes per method.

### Integration tests (`OpcUaBridgeExecTest`, in-process `PlcServer` via `PlcServerFixture`, fake broker channel)

- Discovery gives each `Methods` method an Exec or not exactly as the table says; `OPCUA_METHOD_FILTER` narrows
  it; no namespace 0 method appears; the summary log line appears.
- Each executable method returns the expected outputs; `Reject(150)` gives `BadInvalidArgument` naming
  `percent`.
- `Slow(20000)` succeeds, past the 15 s `OperationTimeout`.
- Ordering end to end: `Slow(2000)`, `NoArgs`, then a Get of `NoArgsCount` are answered in that order and the Get
  sees the increment. A Set then a Get reads its own write.
- Refusals: unknown method, Exec before discovery, a reply-to failing the guard (no answer).
- The existing `OpcUaBridgeTest`, `OpcUaBridgeSetTest`, `OpcUaBridgeSetRoutingTest` and monitor tests pass,
  changed only where they assert that a refusal is sent at once rather than at its turn.

### Manual end-to-end

Run `OpcUaServer` and the bridge against a broker, open the bridge in StatePipes.Explorer, and call each
`Methods` method.

## Docs

- `StatePipes.OpcUaBridge/README.md`: Exec and ExecResult messages; `OPCUA_METHOD_FILTER`; bridge-wide ordering
  of every command and its answer; no call timeout, so a hung method stalls commands (not notifications);
  outcome-unknown codes; a lost answer puts order-based matching out of step; which methods get no Exec and why;
  measured self-description size per method.
- `OpcUaServer/README.md`: the `Methods` nodes.

## Known limitations

- Correlation of ExecResult to Exec is by order only.
- One command at a time, bridge-wide: a slow method delays every other command.
- No call timeout: a hung method stalls commands until the session drops.
- The bridge holds a second OPC UA session on the server once a method has been called.
- Self-description grows with every method; large servers need `OPCUA_METHOD_FILTER`.
