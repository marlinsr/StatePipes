# OPC UA Bridge Set — Design

**Date:** 2026-10-16
**Branch:** `opcua-bridge`
**Component:** `StatePipes.OpcUaBridge` (plus test nodes in `OpcUaServer`)

## Goal

Let StatePipes clients write OPC UA variables through the bridge. For every data item the bridge already
exposes that the session may write, it also exposes a Set command carrying the value to write and a SetStatus
event that tells the sender whether the write succeeded. Both operators (Explorer / HMIs) and automated
services send Sets, through the same messages.

## Requirements (as agreed)

1. Every writable data item gets a runtime Set command whose body holds `Value`, the value to write.
2. On receipt the bridge writes `Value` to the item and answers the sender with that item's SetStatus event,
   saying whether the write succeeded and, if not, why.
3. Set is always on. The session's OPC UA `UserAccessLevel` decides what is writable; an optional
   `OPCUA_WRITE_FILTER` regex narrows it further.
4. SetStatus is a response to the sender only; it is never published on the event exchange.
5. SetStatus is a per-item type, like MonitorStatus.
6. Settable values: natural-typed scalars and one-dimensional arrays, decodable structures, and
   one-dimensional arrays of decodable structures.
7. Every Set and SetStatus is in the data item's namespace, beside its Get, Monitor, Delete and MonitorStatus.
8. `OpcUaServer` gains writable nodes, which the simulator never changes, for manual and e2e testing.

## Non-goals

- A Changed event with the previous and current value (dropped: OPC UA has no atomic read-modify-write).
- Partial array writes (OPC UA `IndexRange`). Every Set replaces the whole value.
- Writing matrices, variables of unfixed rank, abstract types (Number, Integer, UInteger), or any item
  published as a string fallback (NodeId, LocalizedText, QualifiedName, undecodable structures, ...).
- Write-only variables (no `CurrentRead`). They stay undiscovered, as today.
- A correlation id on Set / SetStatus. Clients correlate by per-node order (see Ordering); add an echoed
  `CorrelationId` later if that proves too weak.
- An audit trail of who wrote what. That belongs to the planned authorization work (`OriginalIdentity`,
  audit logging).
- Retrying failed writes, or holding Sets until an OPC UA session returns.

## Message contracts

### Per-item messages

Built like the Monitor messages: the `Get_` prefix of the item's name is swapped, keeping any collision
suffix. For `ns=2;s=Tank.Level` on exchange `Line1Plc`:

| Message | Full name |
|---|---|
| Set command | `Line1Plc.Set_ns2_s_Tank_Level_Command` |
| SetStatus event | `Line1Plc.SetStatus_ns2_s_Tank_Level_Event` |

`MessageNames` gains `SetPrefix = "Set_"`, `SetStatusPrefix = "SetStatus_"` and the matching name properties.
`SetStatus_..._Event` is shorter than `MonitorStatus_..._Event`, so `LongestNameExtra` and every existing
name are unchanged. `MessageNames.FromCommandFullName` also recognises `Set_` (see Error handling).

**Set command**

| Property | Type | Notes |
|---|---|---|
| `Value` | the item's value type, exactly as the Get event's `Value` | Not nullable. A natural type or 1-D array of one, a structure type, or an array of a structure type, reusing the Get event's structure descriptions. |

**SetStatus event**

| Property | Type | Notes |
|---|---|---|
| `NodeId` | string | The item written, in standard notation (`ns=2;s=Tank.Level`). |
| `StatusCode` | uint | The OPC UA Write result, or the bridge's reason for refusing. |
| `Status` | string | `StatusCode` as text. |

### Responses

SetStatus goes to the sender's `ResponseExchangeName` on the main connection channel, like a Get event,
guarded by `ReplyToGuard`. Every Set that passes the guard gets exactly one SetStatus.

### Ordering

Sets to one node are written in the order they arrive, and their SetStatus events are sent in that order.
A client may correlate a SetStatus with its Set by that order.

### Self-description size

Only writable items add the two types. Natural-typed items are expected to add a few KB each; structure items
more, because each type serialization stands alone and repeats its structure descriptions. A test measures
the real figure, which goes in the README. The existing size warning in `OpcUaBridgeService.SetCatalog`
covers the limit; `OPCUA_NODE_FILTER` and `OPCUA_WRITE_FILTER` are the mitigations.

## Components

### Changes to existing components

- **`BridgeSettings`:** `WriteFilter` (`Regex?`) from `OPCUA_WRITE_FILTER`, built like `NodeFilter`. Unset
  means every item passes.
- **`OpcUaDataItem`:** gains `BuiltInType` (the exact type to write) and `IsWritable`, plus
  `SetCommandTypeFullName` and `SetStatusEventTypeFullName`.
- **`StructureLayout`:** gains `BinaryEncodingId` (`NodeId`), from `StructureDefinition.DefaultEncodingId`,
  set by `StructureLayoutBuilder`. Null when the definition has none.
- **`OpcUaClient`:**
  - `ReadVariableAttributesAsync` keeps each item's `BuiltInType` and decides `IsWritable` (see Discovery).
  - `WriteValuesAsync(IReadOnlyList<(NodeId NodeId, Variant Value)>, CancellationToken)` returns one
    `StatusCode` per entry, or `BadNotConnected` for all when there is no connected session.
- **`OpcUaBridgeService`:** the catalog gains `BySetCommand`. `ConsumeCommand` routes a Set through
  `TryGetAllowedReplyTo` to `SetWriter.Enqueue`. `RunAsync` also runs `SetWriter.RunAsync`. It exposes
  sending a SetStatus to `SetWriter`.
- **`SelfDescriptionBuilder`:** adds the Set command and SetStatus event for writable items.
- **`MessageNames` / `OpcUaNaming`:** as under Per-item messages.

### `OpcUaValueEncoder` (new, static)

Turns a JSON `Value` into a `Variant` of the item's exact `BuiltInType`, scalar or 1-D array. Conversion is
strict:

- JSON of the wrong kind (string for a number, fraction for an integer type, object for a scalar) →
  `BadTypeMismatch`.
- A number outside the target type's range → `BadOutOfRange`. Never wraps or rounds.
- DateTime is normalised to UTC. byte[] is read from base64, as Get publishes it.
- Enumeration is written as Int32; StatusCode as `StatusCode`.

### `StructureEncoder` (new, static)

The mirror of `StructureDecoder`. Walks the `StructureLayout` with a `BinaryEncoder`: fields in order, nested
structures inline, arrays as an Int32 length followed by the elements. Wraps the bytes as
`ExtensionObject(layout.BinaryEncodingId, bytes)`; an array of structures becomes `ExtensionObject[]`.

- A missing string, ByteString, array or other reference-typed field is encoded as null, matching how the
  decoder leaves null fields out, so a Get event's `Value` can be sent back unchanged as a Set.
- A missing value-type field (e.g. `Volume`) → `BadTypeMismatch`. Never defaulted.
- A property not in the layout → `BadTypeMismatch`, so a misspelled field is never silently dropped.
- Number range and kind rules as in `OpcUaValueEncoder`.

### `SetWriter` (new)

Owns the Set queue, encoding, batching and answers.

- `Enqueue(OpcUaDataItem item, byte[] body, BusConfig replyTo, byte[] replyToRaw)`, called on the transport's
  receive thread; it only writes to an unbounded single-reader `Channel<PendingSet>`.
- `RunAsync(CancellationToken)`: the single reader.
- Depends on an `ISetSession`-style seam (`WriteValuesAsync`) implemented by `OpcUaClient` and on an output
  for SetStatus implemented by `OpcUaBridgeService`, so it is unit-tested with fakes.

## Data flow

### Discovery

In `ReadVariableAttributesAsync`, for each readable variable, `IsWritable` is true when all hold:

1. `UserAccessLevel` has `CurrentWrite` (the user's rights, not `AccessLevel`).
2. The NodeId text matches `WriteFilter`, or `WriteFilter` is unset.
3. The value has a writable shape:
   - a natural CLR type (`OpcUaValueMapper.MapScalar` non-null), ValueRank `Scalar` or `OneDimension`, and
     not one of the abstract types Number, Integer, UInteger; or
   - a decodable structure (`Structure != null`), scalar or 1-D, whose layout and every nested layout has a
     `BinaryEncodingId`.

An item with `CurrentWrite` that fails 2 is filtered silently. One that fails 3 is counted by reason
(matrix or unfixed rank, abstract type, string fallback, no binary encoding), and discovery logs one Info
summary, e.g. `3 writable items have no Set command (matrix: 1, abstract type: 1, string fallback: 1)`, with
each item and reason at Verbose.

### Set command

1. `ConsumeCommand` finds the routing key in `BySetCommand`.
2. `TryGetAllowedReplyTo`; on failure the Set is dropped unanswered and logged. Nothing is written.
3. `SetWriter.Enqueue`.

### Serving Sets

The reader loops:

1. Drain up to 500 pending Sets into a batch, starting with any Set held over from the last batch. **Stop at
   the first Set whose node is already in the batch** and hold it over, so Sets to one node are never in one
   Write and are applied in arrival order.
2. Encode each Set's `Value` (`OpcUaValueEncoder` or `StructureEncoder`). A Set that fails is answered at once
   with its status and left out of the Write.
3. One `WriteValuesAsync` for the rest.
4. Send each Set's SetStatus with its result, in batch order.

No Set is ever retried.

## Error handling

| Situation | Behaviour | SetStatus `StatusCode` |
|---|---|---|
| Reply-to fails `ReplyToGuard` | Dropped and logged, nothing written | none sent |
| Set before the first discovery | Recognised by `FromCommandFullName` | `BadWaitingForInitialData` |
| Set for a node the catalog does not hold | Recognised by `FromCommandFullName` | `BadNodeIdUnknown` |
| Set for a readable item without a Set (read-only, unwritable shape, filtered) | Item in `ByCommand`, not in `BySetCommand` | `BadNotWritable` |
| No `Value`, wrong JSON kind, missing value-type field, unknown field | Refused before the Write | `BadTypeMismatch` |
| Number outside the type's range | Refused before the Write | `BadOutOfRange` |
| No connected OPC UA session | Fails fast, no waiting for reconnect | `BadNotConnected` |
| Write throws | Logged as an error; every Set in the batch gets the code | the exception's `StatusCode`, else `BadCommunicationError` |
| Server rejects a node | Passed through | the server's code (`BadOutOfRange`, `BadUserAccessDenied`, ...) |
| Server accepts | | `Good` |
| Rediscovery while Sets are queued | A queued Set keeps its item and writes by NodeId | the server's result |
| Broker down when SetStatus is sent | Logged and lost, as a Get event is today | none sent |

`BadTimeout` and communication errors mean **the outcome is unknown**: the write may have reached the server.
A client that must know sends a Get.

**Logging:** failed Sets at Warning (node, sender `CommandExchangeName`, status); successful Sets at Verbose.

## `OpcUaServer` test nodes

The existing writable `pressure`, `msg` and `Density` stay as they are; `Worker` overwrites them every 500 ms.
A new `PLC_Data/Setpoints` folder holds nodes `Worker` never changes:

| NodeId (`ns=2;s=...`) | Type | Expected bridge result |
|---|---|---|
| `Setpoints.Double` | Double | Set |
| `Setpoints.Int16` | Int16 | Set (exact built-in type) |
| `Setpoints.Boolean` | Boolean | Set |
| `Setpoints.String` | String | Set |
| `Setpoints.DateTime` | DateTime | Set |
| `Setpoints.DoubleArray` | Double[] | Set |
| `Setpoints.Int32Array` | Int32[] | Set |
| `Setpoints.Density` | DensityDataType | Set |
| `Setpoints.DensityArray` | DensityDataType[] | Set |
| `Setpoints.Tank` | TankDataType | Set (nested structure, arrays) |
| `Setpoints.Percent` | Double; write handler rejects values outside 0–100 | Set; `BadOutOfRange` on 150 |
| `Setpoints.ReadOnly` | Double, `CurrentRead` | no Set |
| `Setpoints.UserReadOnly` | Double, AccessLevel read/write, UserAccessLevel read | no Set |
| `Setpoints.Matrix` | Double, ValueRank 2 | no Set |
| `Setpoints.Number` | abstract Number | no Set |
| `Setpoints.Text` | LocalizedText | no Set |

`PlcServerFixture` exposes these NodeIds for tests.

## Testing

### Unit tests (`StatePipes.Test/OpcUaBridge`)

- Naming (beside the existing naming tests in `OpcUaBridgeTest`): Set and SetStatus names, collision suffix
  carried over, longest name within 255 bytes, `FromCommandFullName` recognises `Set_`.
- `OpcUaValueEncoderTest`: each natural type to its exact `BuiltInType`; `3.7` into Int16 and a string into a
  double → `BadTypeMismatch`; 70000 into Int16 → `BadOutOfRange`; missing `Value`; UTC DateTime; base64
  byte[]; arrays.
- `StructureEncoderTest`: round trip `decode(encode(json)) == json` for Density, DensityArray and Tank; missing
  reference-typed field encodes null; missing value-type field and unknown field → `BadTypeMismatch`.
- `SetWriterTest` (fake session and output): one Write per batch; batch cut at a repeated node; per-node order
  preserved; encoding failure answered without a Write; `BadNotConnected` fails fast; a Write exception gives
  every Set in the batch the code; per-node results reach the right SetStatus.
- `SelfDescriptionBuilder`: Set and SetStatus only for writable items; the Explorer-consumption test extended
  so emitted Set types serialize into bodies the encoders accept; a size test recording bytes per writable
  item.

### Integration tests (`OpcUaBridgeSetTest`, in-process `PlcServer` via `PlcServerFixture`, fake broker channel)

- Discovery gives each `Setpoints` node a Set or not exactly as the table says; `OPCUA_WRITE_FILTER` narrows
  it; the summary log line appears.
- Set then Get on each writable node: SetStatus `Good`, Get returns the written value, server type unchanged.
- `Setpoints.Percent` = 150 → `BadOutOfRange`, value unchanged.
- Set for an unknown node, a non-writable node and before discovery → the codes above; a reply-to failing the
  guard gets no answer and the value is unchanged.

### Manual end-to-end

Run `OpcUaServer` and the bridge against a broker, open the bridge in StatePipes.Explorer, Set each
`Setpoints` node, and confirm with Get.

## Docs

- `StatePipes.OpcUaBridge/README.md`: Set / SetStatus messages, `OPCUA_WRITE_FILTER`, per-node ordering,
  "BadTimeout means outcome unknown", which items get no Set and why, measured self-description size per
  writable item.
- `OpcUaServer`: a note listing the `Setpoints` nodes.

## Known limitations

- Correlation of SetStatus to Set is by per-node order only.
- Whole-value writes only; no `IndexRange`.
- Self-description grows with every writable item; large servers need the filters.
