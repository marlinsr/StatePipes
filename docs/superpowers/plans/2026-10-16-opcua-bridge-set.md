# OPC UA Bridge Set Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose a per-item `Set_…_Command` for every writable OPC UA data item, write its `Value` to the server, and answer the sender with a per-item `SetStatus_…_Event`.

**Architecture:** Discovery marks items writable and keeps their exact `BuiltInType`. `OpcUaBridgeService` routes Set commands to a new `SetWriter`, which encodes JSON to `Variant` (`OpcUaValueEncoder` for natural types, `StructureEncoder` for structures), batches Sets into order-preserving OPC UA Writes through `OpcUaClient.WriteValuesAsync`, and sends one SetStatus per Set back through the bridge.

**Tech Stack:** .NET 10, OPCFoundation.NetStandard.Opc.Ua.Client 2.0.1, Newtonsoft.Json, MSTest 4.

**Spec:** `docs/superpowers/specs/2026-10-16-opcua-bridge-set-design.md`

## Global Constraints

- Names: `Set_` + base + `_Command`, `SetStatus_` + base + `_Event`, sharing the Get name's base and collision suffix; `MessageNames.LongestNameExtra` stays 8.
- `MessageNames.FromCommandFullName` is **unchanged** (it drives MonitorStatus answers). Set gets its own `MessageNames.FromSetCommandFullName`. This is how the spec's "recognises `Set_`" is implemented without misrouting Monitor traffic.
- SetStatus body: `NodeId` (string), `StatusCode` (uint), `Status` (string), using the `SelfDescriptionBuilder.*Property` constants.
- Set body: `Value` only; not nullable in the self-description.
- Status codes: `BadTypeMismatch` (no/wrong `Value`, missing value-type field, unknown field), `BadOutOfRange` (number out of range), `BadNotWritable` (readable item without Set), `BadNodeIdUnknown`, `BadWaitingForInitialData`, `BadNotConnected`, `BadCommunicationError` (Write threw a non-`ServiceResultException`).
- Batch: at most 500 Sets per Write; a batch never holds two Sets for the same NodeId; no retries; no waiting for reconnect.
- A structure is writable only if it, and every nested layout, has a `BinaryEncodingId` **and** every non-structure field has a natural CLR type (`OpcUaValueMapper.MapToClrType(field.BuiltInType, Scalar) != typeof(string)` or the field's `BuiltInType` is `String`). Otherwise it counts as "no binary encoding" or "string fallback". (Fills a spec gap: fallback-typed fields have no faithful inverse.)
- `OPCUA_WRITE_FILTER` regex matched against NodeId text, like `OPCUA_NODE_FILTER`.
- Logging: failed Set → Warning with node, sender `CommandExchangeName`, status; successful Set → Verbose.
- Every `dotnet` call needs `MsBuildSDKsPath="C:\Program Files\dotnet\sdk\10.0.402\Sdks"`. Build with `StatePipes.sln`. Run `dotnet format StatePipes.sln` before each commit (CLAUDE.md rule 3).
- Test command: `dotnet test StatePipes.Test --settings tests.runsettings --filter "FullyQualifiedName~StatePipes.Test.OpcUaBridge"`.
- Integration tests use `PlcServerFixture.StartAsync(48403)` (48401/48402 are taken).

## Review Focus

1. **A client sends a Get event's `Value` straight back as a Set** (structure with a null string field omitted): accepted and round-trips — Task 4 `Encode_DecodedGetValueWithNullFieldOmitted_RoundTrips`, Task 8 `SetThenGet_EachWritableSetpoint_ReturnsWrittenValue`.
2. **JSON integer `5` for a Double item**: accepted as 5.0, not `BadTypeMismatch` — Task 3 `Encode_IntegerJsonForDouble_Accepted`.
3. **Two Sets to one node sent back-to-back**: the server ends with the last value and statuses come back in send order — Task 6 `Batch_RepeatedNode_SplitsAndPreservesOrder`, Task 8 `Set_TwiceQuickly_LastValueWins`.
4. **DateTime without an offset** (`"2026-01-01T00:00:00"`): treated as UTC, not local — Task 3 `Encode_DateTimeWithoutOffset_TreatedAsUtc`.
5. **Writing Int16 leaves the server variable Int16** (no `Int32` substitution) — Task 8 `SetThenGet_EachWritableSetpoint_ReturnsWrittenValue` reads back `Variant.TypeInfo.BuiltInType`.

---

### Task 1: `Setpoints` test nodes in OpcUaServer

**Files:**
- Modify: `OpcUaServer/PlcNodeManager.cs` (`CreateAddressSpace`, new private helper)
- Modify: `OpcUaServer/README.md` if present, else create it with the node table
- Modify: `StatePipes.Test/OpcUaBridge/PlcServerFixture.cs`
- Test: `StatePipes.Test/OpcUaBridge/OpcUaBridgeSetTest.cs` (new, `[TestCategory(TestCategories.Integration)]`)

**Interfaces — Produces:**
- Folder `ns=2;s=Setpoints` (Organizes from `PLC_Data`). Child NodeIds `Setpoints.Double`, `.Int16`, `.Boolean`, `.String`, `.DateTime`, `.DoubleArray`, `.Int32Array`, `.Density`, `.DensityArray`, `.Tank`, `.Percent`, `.ReadOnly`, `.UserReadOnly`, `.Matrix`, `.Number`, `.Text` with the types and access levels in the spec's table. `Worker` never touches them.
- `Setpoints.Percent`: `OnWriteValue` handler returns `StatusCodes.BadOutOfRange` for values outside 0–100.
- `Setpoints.Matrix`: `ValueRank = 2`, `ArrayDimensions = [2, 2]`. `Setpoints.Number`: `DataType = DataTypeIds.Number`, value `0.0`.
- `PlcServerFixture.Setpoint(string name) => new NodeId($"Setpoints.{name}", NamespaceIndex)`.

- [ ] **Step 1: Write the failing test** in `OpcUaBridgeSetTest` (class init on port 48403, discovery through `new OpcUaClient(_plc.CreateBridgeSettings(MonitorRegistryTest.Own))`):

```csharp
[TestMethod]
public async Task Discover_Setpoints_ReadableWithNaturalTypes()
{
    var items = (await DiscoverAsync()).ToDictionary(i => i.NodeId);
    Assert.AreEqual(typeof(short), items[_plc.Setpoint("Int16")].ValueType);
    Assert.AreEqual(typeof(double[]), items[_plc.Setpoint("DoubleArray")].ValueType);
    Assert.IsNotNull(items[_plc.Setpoint("Density")].Structure);
    Assert.IsTrue(items[_plc.Setpoint("DensityArray")].IsArray);
    Assert.AreEqual(typeof(string), items[_plc.Setpoint("Matrix")].ValueType);
    Assert.HasCount(16, items.Keys.Where(n => n.IdentifierAsString?.StartsWith("Setpoints.") == true));
}
```

- [ ] **Step 2: Run it** — FAIL, `Setpoints.Int16` not found.
- [ ] **Step 3: Add the folder and nodes** with one helper `AddSetpoint<T>(FolderState folder, string name, NodeId dataType, int valueRank, T value, byte accessLevel, byte userAccessLevel)` returning the `BaseDataVariableState<T>`; structures use `new ExtensionObject(DensityData.BinaryEncodingId, …)` like `_density`. Add the fixture helper and the README node table.
- [ ] **Step 4: Run it** — PASS; run the whole OpcUaBridge filter — existing tests still PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-server): Setpoints nodes for testing writes`.

### Task 2: Names, data item, layout encoding id, write filter

**Files:**
- Modify: `StatePipes.OpcUaBridge/OpcUaNaming.cs` (`MessageNames`), `OpcUaDataItem.cs`, `StructureLayout.cs`, `StructureLayoutBuilder.cs`, `BridgeSettings.cs`
- Test: `StatePipes.Test/OpcUaBridge/OpcUaBridgeTest.cs` (naming), `OpcUaStructureTest.cs` (layout)

**Interfaces — Produces:**
- `MessageNames.SetPrefix = "Set_"`, `SetStatusPrefix = "SetStatus_"`; `SetCommandTypeName/FullName`, `SetStatusEventTypeName/FullName`.
- `static MessageNames? MessageNames.FromSetCommandFullName(string commandFullName, string @namespace)` — `{ns}.Set_X_Command` → `new MessageNames(ns, "Get_X")`; null otherwise.
- `OpcUaDataItem(..., bool IsArray = false, BuiltInType BuiltInType = BuiltInType.Null, bool IsWritable = false)` plus `SetCommandTypeFullName`, `SetStatusEventTypeFullName`.
- `StructureLayout(NodeId dataTypeId, string @namespace, string name, NodeId? binaryEncodingId = null)` with `NodeId? BinaryEncodingId`; `StructureLayoutBuilder` passes `definition.DefaultEncodingId` (null when `IsNull`).
- `BridgeSettings.WriteFilter` (`Regex?`) from `OPCUA_WRITE_FILTER`, built exactly like `NodeFilter`.

- [ ] **Step 1: Write failing tests**
  - `ToMessageNames_SetNames_ShareBaseAndSuffix`: for `ns=2;s=Tank.Level` on `Line1Plc`, `SetCommandTypeFullName == "Line1Plc.Set_ns2_s_Tank_Level_Command"`, `SetStatusEventTypeFullName == "Line1Plc.SetStatus_ns2_s_Tank_Level_Event"`; with `Name = "Get_ns2_s_X_2"` → `Set_ns2_s_X_2_Command`.
  - `ToMessageNames_VeryLongIdentifier_SetStatusFitsRoutingKey`: `SetStatusEventTypeFullName.Length <= 255`.
  - `FromSetCommandFullName_RecognisesSetOnly`: Set name → names whose `SetStatusEventTypeFullName` is the expected one; Monitor, Delete and Get names → null; `FromCommandFullName` of a Set name → null.
  - `BuildLayout_RecordsDefaultEncodingId`: give `DensityDefinition` `DefaultEncodingId = new NodeId("DensityDataType_Encoding_DefaultBinary", 2)`; layout's `BinaryEncodingId` equals it; a definition without one → `BinaryEncodingId` null.
- [ ] **Step 2: Run** — FAIL (members missing).
- [ ] **Step 3: Implement** the members above.
- [ ] **Step 4: Run the OpcUaBridge filter** — all PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): Set and SetStatus names, writable item fields`.

### Task 3: `OpcUaValueEncoder`

**Files:**
- Create: `StatePipes.OpcUaBridge/OpcUaValueEncoder.cs`
- Test: `StatePipes.Test/OpcUaBridge/OpcUaValueEncoderTest.cs`

**Interfaces — Produces:**
- `static StatusCode OpcUaValueEncoder.TryEncode(JToken? value, BuiltInType builtInType, bool isArray, out Variant variant)` — `Good` with a Variant whose `TypeInfo.BuiltInType == builtInType`, or a Bad code and `Variant.Null`.
- `static StatusCode OpcUaValueEncoder.TryConvertScalar(JToken value, BuiltInType builtInType, out object? clrValue)` — the strict scalar conversion `StructureEncoder` reuses; returns CLR values (`short`, `double`, `string`, `DateTime` (UTC), `Guid`, `byte[]`, …).
- Supported `builtInType`s: Boolean, SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Float, Double, String, DateTime, Guid, ByteString, StatusCode, Enumeration. Anything else → `BadTypeMismatch`.

- [ ] **Step 1: Write failing tests** (one `[TestMethod]` each, `[DataRow]` where it fits):
  - `Encode_EachNaturalType_CarriesExactBuiltInType`: e.g. `5`/Int16 → `TypeInfo.BuiltInType == Int16`, `(short)5`; `true`/Boolean; `"abc"`/String; `"AQID"`/ByteString → bytes `1,2,3`; a Guid string/Guid; `12`/Enumeration → Int32 value 12; `0`/StatusCode.
  - `Encode_IntegerJsonForDouble_Accepted`: `5` / Double → `5.0`.
  - `Encode_FractionForInteger_TypeMismatch`: `3.7` / Int16 → `BadTypeMismatch`.
  - `Encode_OutOfRange_BadOutOfRange`: `70000` / Int16, `-1` / UInt32, `300` / Byte → `BadOutOfRange`.
  - `Encode_WrongJsonKind_TypeMismatch`: `"5"` / Double, `{}` / Double, `[1]` scalar Double, `5` / String → `BadTypeMismatch`.
  - `Encode_NullOrMissing_TypeMismatch`: `null` token and `JValue.CreateNull()` → `BadTypeMismatch`.
  - `Encode_DateTimeWithOffset_ConvertedToUtc`: `"2026-01-01T02:00:00+02:00"` → `2026-01-01T00:00:00Z`.
  - `Encode_DateTimeWithoutOffset_TreatedAsUtc`: `"2026-01-01T00:00:00"` → `2026-01-01T00:00:00Z`, `Kind == Utc`.
  - `Encode_Arrays_TypedArrayVariant`: `[1.5, 2]` / Double, isArray → `double[] {1.5, 2.0}` with `TypeInfo.ValueRank == OneDimension`; `[]` → empty array, Good; `[1, "x"]` → `BadTypeMismatch`; `5` with isArray → `BadTypeMismatch`.
- [ ] **Step 2: Run** — FAIL (type missing).
- [ ] **Step 3: Implement.** Parse JSON with `DateParseHandling.None` upstream so dates arrive as strings; parse them with `DateTimeStyles.AdjustToUniversal | AssumeUniversal`. Range-check integers via `JTokenType.Integer` + `BigInteger`/`checked` conversion. Build Variants with the typed 2.0 constructors (`new Variant(short)`, `new Variant(ArrayOf<short>)`, `DateTimeUtc`, `Uuid`, `ByteString`, `StatusCode`); Enumeration as Int32.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): strict JSON to Variant encoding`.

### Task 4: `StructureEncoder`

**Files:**
- Create: `StatePipes.OpcUaBridge/StructureEncoder.cs`
- Test: `StatePipes.Test/OpcUaBridge/StructureEncoderTest.cs` (reuse `OpcUaStructureTest`'s definitions by making its `CreateBuilder`, `Definition`, `Field` helpers `internal static`)

**Interfaces — Consumes:** `OpcUaValueEncoder.TryConvertScalar`; `StructureLayout.BinaryEncodingId` (Task 2).
**Produces:**
- `static StatusCode StructureEncoder.TryEncode(JToken? value, StructureLayout layout, bool isArray, out Variant variant)` — scalar → `Variant(ExtensionObject)`, array → `Variant(ArrayOf<ExtensionObject>)`; each `new ExtensionObject(layout.BinaryEncodingId!, new ByteString(bytes))`.
- `static bool StructureEncoder.CanEncode(StructureLayout layout)` — the writable-structure rule in Global Constraints, recursive and safe on self-referencing layouts.

- [ ] **Step 1: Write failing tests**
  - `Encode_Density_RoundTripsThroughDecoder`: `{"Volume":1.5,"Mass":2}` → `StructureDecoder.ToJToken(variant, layout, false)` deep-equals the input (`JToken.DeepEquals`, with `Mass` as `2.0`).
  - `Encode_TankWithNestedAndArrays_RoundTrips`: Tank with `Name`, `Density`, two `History` elements, `Readings` → round-trips.
  - `Encode_DensityArray_RoundTrips`: `[{…},{…}]`, isArray → round-trips; variant `TypeInfo.ValueRank == OneDimension`.
  - `Encode_DecodedGetValueWithNullFieldOmitted_RoundTrips`: Tank JSON with no `Name` and no `History` → encodes (Good); decoding gives the same JSON (both omitted).
  - `Encode_MissingValueTypeField_TypeMismatch`: `{"Volume":1}` → `BadTypeMismatch`.
  - `Encode_UnknownField_TypeMismatch`: `{"Volume":1,"Mass":2,"Volum":3}` → `BadTypeMismatch`.
  - `Encode_FieldOutOfRange_BadOutOfRange`: an Int16 field with `70000` (add a small test definition) → `BadOutOfRange`.
  - `Encode_NotAnObject_TypeMismatch`: `5`, `null`, and an object for isArray → `BadTypeMismatch`.
  - `CanEncode_Rules`: Tank with encoding ids → true; Tank whose nested Density layout lacks an id → false; a layout with a `NodeId`-typed field → false; the recursive Tree layout with ids → true (terminates).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement** with a `BinaryEncoder` over `ServiceMessageContext.Create(DefaultTelemetry.Create(_ => { }))` (as `StructureDecoder`). Fields in layout order; arrays as Int32 length then elements; a missing reference-typed field (String, ByteString, array) is written as null (`-1` length); a missing value-type field fails. Collect the JSON object's property names and fail if any is not a field name.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): encode structures for writing`.

### Task 5: Discovery marks writable items; `OpcUaClient.WriteValuesAsync`

**Files:**
- Create: `StatePipes.OpcUaBridge/ISetSession.cs`
- Modify: `StatePipes.OpcUaBridge/OpcUaClient.cs` (`DiscoverAsync`, `ReadVariableAttributesAsync`, new `WriteValuesAsync`)
- Test: `StatePipes.Test/OpcUaBridge/OpcUaBridgeSetTest.cs`

**Interfaces — Consumes:** `OpcUaDataItem.BuiltInType/IsWritable`, `BridgeSettings.WriteFilter` (Task 2); `StructureEncoder.CanEncode` (Task 4).
**Produces:**
- `internal interface ISetSession { Task<IReadOnlyList<StatusCode>> WriteValuesAsync(IReadOnlyList<(NodeId NodeId, Variant Value)> values, CancellationToken ct); }` implemented by `OpcUaClient`: one `session.WriteAsync` with `WriteValue { AttributeId = Attributes.Value, Value = new DataValue(variant) }`; when not connected returns `BadNotConnected` for every entry without calling the server. Exceptions propagate (SetWriter maps them).
- Items carry `BuiltInType` and `IsWritable` per the spec's Discovery rules. Reasons counted: `matrix or unfixed rank`, `abstract type`, `string fallback`, `no binary encoding`; one Info line `"{n} writable items have no Set command ({reason}: {count}, ...)"` only when n > 0, each item + reason at Verbose.

- [ ] **Step 1: Write failing tests** in `OpcUaBridgeSetTest`:
  - `Discover_Setpoints_WritableExactlyAsSpecified`: `IsWritable` true for Double, Int16, Boolean, String, DateTime, DoubleArray, Int32Array, Density, DensityArray, Tank, Percent; false for ReadOnly, UserReadOnly, Matrix, Number, Text. `Int16` item's `BuiltInType == BuiltInType.Int16`.
  - `Discover_WriteFilter_NarrowsWritable`: settings with `WriteFilter = new Regex("Setpoints\\.Double$")` → only `Setpoints.Double` writable among Setpoints; `ReadOnly` still readable.
  - `WriteValues_WritesAndReturnsPerNodeResults`: write `new Variant(42.0)` to Double and `new Variant(150.0)` to Percent → `[Good, BadOutOfRange]`; reading Double gives 42.
  - `WriteValues_NotConnected_AllBadNotConnected`: a fresh unconnected client → `[BadNotConnected, BadNotConnected]`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.** In `ReadVariableAttributesAsync` keep `builtInType` alongside the tuple and decide writability there (`AccessLevels.CurrentWrite` on `UserAccessLevel`, `WriteFilter`, then shape). Abstract = `BuiltInType` is `Number`, `Integer` or `UInteger`.
- [ ] **Step 4: Run** the OpcUaBridge filter — PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): discover writable items and write values`.

### Task 6: `SetWriter`

**Files:**
- Create: `StatePipes.OpcUaBridge/SetWriter.cs` (with `internal interface ISetOutput`)
- Test: `StatePipes.Test/OpcUaBridge/SetWriterTest.cs` (`[TestCategory(TestCategories.Unit)]`, fake `ISetSession` recording each batch and returning scripted results or throwing; fake `ISetOutput` recording statuses)

**Interfaces — Consumes:** `ISetSession` (Task 5), `OpcUaValueEncoder.TryEncode` (Task 3), `StructureEncoder.TryEncode` (Task 4).
**Produces:**
- `internal interface ISetOutput { void SendSetStatus(OpcUaDataItem item, StatusCode statusCode, BusConfig replyTo, byte[] replyToRaw); }`
- `SetWriter(ISetSession session, ISetOutput output)`; `void Enqueue(OpcUaDataItem item, byte[] body, BusConfig replyTo, byte[] replyToRaw)` (never blocks); `Task RunAsync(CancellationToken ct)`; `void Complete()` (completes the queue on dispose).
- Encoding: parse `body` as a `JObject` (`DateParseHandling.None`); non-object body or missing/null `Value` → `BadTypeMismatch`; `item.Structure != null` → `StructureEncoder.TryEncode(value, item.Structure, item.IsArray, …)`, else `OpcUaValueEncoder.TryEncode(value, item.BuiltInType, item.IsArray, …)`.
- Write exception: `ServiceResultException` → its `StatusCode`; anything else → `BadCommunicationError`; logged as an error.

- [ ] **Step 1: Write failing tests**
  - `Sets_QueuedTogether_OneWrite`: enqueue 3 Sets to different nodes before `RunAsync` starts → session sees one batch of 3; output gets 3 `Good` in enqueue order.
  - `Batch_RepeatedNode_SplitsAndPreservesOrder`: enqueue A=1, B=1, A=2, A=3 before start → batches `[A=1, B=1]`, `[A=2]`, `[A=3]`; statuses in enqueue order.
  - `Batch_Over500_Split`: 501 distinct nodes → batches of 500 and 1.
  - `EncodingFailure_AnsweredWithoutWrite`: `{"Value":"x"}` for a Double item → `BadTypeMismatch`, no Write call; `{}` → `BadTypeMismatch`; body `not json` → `BadTypeMismatch`.
  - `EncodingFailureInBatch_OthersStillWritten`: a bad and a good Set → one Write with only the good one; both answered.
  - `NotConnected_PassedThrough`: session returns `BadNotConnected` → status `BadNotConnected`.
  - `WriteThrows_EverySetInBatchGetsCode`: throw `ServiceResultException(BadTimeout)` → both Sets `BadTimeout`; throw `InvalidOperationException` → `BadCommunicationError`; the next batch still runs.
  - `PerNodeResults_ReachTheRightSet`: results `[Good, BadUserAccessDenied]` map to the matching items.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.** Unbounded `Channel<PendingSet>` (`SingleReader = true`). The batching loop:

```csharp
PendingSet? held = null;
while (held != null || await reader.WaitToReadAsync(ct))
{
    List<PendingSet> batch = []; HashSet<NodeId> nodes = [];
    if (held != null) { batch.Add(held); nodes.Add(held.Item.NodeId); held = null; }
    while (batch.Count < MaxWriteBatch && reader.TryRead(out var next))
    {
        if (!nodes.Add(next.Item.NodeId)) { held = next; break; }
        batch.Add(next);
    }
    await WriteBatchAsync(batch, ct); // encode, answer failures, one WriteValuesAsync, answer the rest in batch order
}
```

  Log failures at Warning (`{nodeId} Set from {replyTo.CommandExchangeName}: {status}`), successes at Verbose.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): SetWriter batches order-preserving writes`.

### Task 7: Bridge routing and self-description

**Files:**
- Modify: `StatePipes.OpcUaBridge/OpcUaBridgeService.cs`, `SelfDescriptionBuilder.cs`, `ServiceWorker.cs` (constructor change only, if any)
- Test: `StatePipes.Test/OpcUaBridge/OpcUaBridgeSetRoutingTest.cs` (Unit; reuse `FakeChannelFactory`, `OpcUaBridgeMonitorTest.Client`/`ClientReplyTo`), `OpcUaStructureTest.cs`, `OpcUaBridgeTest.cs`

**Interfaces — Consumes:** `SetWriter`, `ISetOutput`, `ISetSession` (Tasks 5–6); `MessageNames.FromSetCommandFullName` (Task 2).
**Produces:**
- `OpcUaBridgeService` implements `ISetOutput`: sends `{NodeId, StatusCode, Status}` as `item.SetStatusEventTypeFullName` via `SendResponse(_connectionChannel, …)`. For unknown names it sends with `NodeId = ""` on the name from `FromSetCommandFullName`.
- Constructor gains optional `ISetSession? setSession = null` (default `opcUa`) so routing tests can fake writes.
- `Catalog.BySetCommand` (writable items only). `ConsumeCommand` order: self-description, Get, Monitor, Delete, **Set** (`TryGetAllowedReplyTo` → `SetWriter.Enqueue`), **Set name not in `BySetCommand`** (`TryGetAllowedReplyTo`, then: not discovered → `BadWaitingForInitialData`; item in `ByCommand` → `BadNotWritable` with its NodeId; else `BadNodeIdUnknown`), then the existing Monitor/Delete fallback.
- `RunAsync` adds `SetWriter.RunAsync`; `Dispose` completes it. `SetCatalog` log line says "Get, Monitor, Delete and Set (n writable)".
- `SelfDescriptionBuilder.SetProperty` const not needed — `Value` reuses `ValueProperty`. `BuildSetCommand(item)`: `IsCommand`, `Value` as the Get event's Value but `isNullable: false`. `BuildSetStatusEvent(item)`: `NodeId` string, `StatusCode` uint, `Status` string. Both added only when `item.IsWritable`.

- [ ] **Step 1: Write failing tests**
  - Routing (fake session):
    - `Set_WritableItem_WrittenAndAnsweredOnMainChannel`: catalog with writable Double item; send `{"Value":4.5}` → fake session got `(nodeId, 4.5)`; SetStatus on `Main` to `Client.ResponseExchangeName`, `ReplyToRaw == ClientReplyTo`, body `StatusCode == 0`, `NodeId == "ns=2;s=x"`.
    - `Set_BeforeDiscovery_BadWaitingForInitialData`; `Set_UnknownNode_BadNodeIdUnknown`; `Set_ReadableButNotWritable_BadNotWritable` (NodeId filled); none call the session.
    - `Set_ReplyToFailsGuard_NoWriteNoAnswer`: reply-to with another `ClientCertPath` → no Write, nothing sent with a `SetStatus_` routing key.
    - `MonitorCommand_UnknownNode_StillAnsweredWithMonitorStatus`: regression that the Set fallback does not swallow Monitor names.
  - Self-description:
    - `SelfDescription_SetMessagesOnlyForWritableItems`: one writable, one not → `Set_`/`SetStatus_` types only for the writable one; Set `Value` property `IsNullable == false`.
    - Extend `SelfDescription_StructuredValues_EmitTypesThatGetEventsDeserializeInto` (or add a sibling): emit like Explorer, instantiate the Set type for Density, serialize it, and `StructureEncoder.TryEncode(JObject.Parse(json)["Value"], …)` → Good.
    - `SelfDescription_SizePerWritableItem`: build for 100 writable Double items and 100 read-only ones; log the per-item delta with `Console.WriteLine` and assert it is under 6 KB (record the measured figure for Task 8's README).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement** the above.
- [ ] **Step 4: Run the OpcUaBridge filter** — PASS (monitor tests unaffected).
- [ ] **Step 5: `dotnet format`, commit** `feat(opcua-bridge): route Set commands and describe them`.

### Task 8: End-to-end Set and docs

**Files:**
- Modify: `StatePipes.Test/OpcUaBridge/OpcUaBridgeSetTest.cs` (bridge harness like `OpcUaBridgeMonitorRecoveryTest.Bridge`)
- Modify: `StatePipes.OpcUaBridge/README.md`
- Modify: `C:\Users\shawn\.claude\projects\C--Users-shawn-source-repos-StatePipes\memory\opcua-bridge.md` (Set design decisions)

**Interfaces — Consumes:** everything above, via `ServiceWorker.MaintainOpcUaSessionAsync` and `FakeChannelFactory`.

- [ ] **Step 1: Write failing tests** (wait for discovery by polling a Get until its event has `StatusCode == 0`):
  - `SetThenGet_EachWritableSetpoint_ReturnsWrittenValue` (`[DataRow]` per node with a JSON value: Double `12.5`, Int16 `-7`, Boolean `true`, String `"hi"`, DateTime `"2026-03-04T05:06:07Z"`, DoubleArray `[1,2.5]`, Int32Array `[3,4]`, Density `{"Volume":1,"Mass":2}`, DensityArray two elements, Tank full, Tank with `Name`/`History` omitted) → SetStatus `StatusCode == 0`; the next Get's `Value` deep-equals the sent value (doubles normalised); for Int16 also read the node directly through the fixture's node manager and assert the stored value is `short`.
  - `Set_PercentOutOfRange_BadOutOfRangeValueUnchanged`: Set 50, then 150 → second SetStatus `BadOutOfRange`; Get returns 50.
  - `Set_TwiceQuickly_LastValueWins`: two Sets to Double (1 then 2) delivered back-to-back → two SetStatus events, both Good, in order; Get returns 2.
  - `Set_ReadOnlySetpoint_BadNotWritable`.
- [ ] **Step 2: Run** — FAIL until the harness is written; then confirm they PASS against Tasks 1–7. A failure here is a bug in an earlier task: fix it there, with a unit test.
- [ ] **Step 3: README** — Set/SetStatus message section beside the Monitor section; `OPCUA_WRITE_FILTER` in the settings table; per-node ordering; "`BadTimeout` and communication errors mean the outcome is unknown — send a Get"; which items get no Set and the discovery log line; measured self-description size per writable item from Task 7.
- [ ] **Step 4: Full test run** `dotnet test StatePipes.Test --settings tests.runsettings` — PASS; `dotnet build StatePipes.sln` — no new warnings.
- [ ] **Step 5: Update the memory file**, `dotnet format`, commit `test(opcua-bridge): end-to-end Set; docs`.
