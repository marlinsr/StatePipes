# OPC UA Bridge Monitors — Design

**Date:** 2026-10-15
**Branch:** `opcua-bridge`
**Component:** `StatePipes.OpcUaBridge`

## Goal

Let StatePipes clients subscribe to OPC UA data changes through the bridge instead of polling Get commands.
For every readable data item the bridge already exposes as a Get command / Get event pair, it also exposes a
Monitor command. A client sends a Monitor command to set up an OPC UA monitored item; every data change the
OPC UA server reports for it is sent back to that client as the item's existing Get event. Monitors survive
both OPC UA server reboots and bridge restarts, and a client can update or delete its monitors by name.

## Requirements (as agreed)

1. Every readable data item gets a runtime Monitor command whose body defines: monitor name, monitoring mode
   {Disabled, Reporting}, DataChangeTrigger {Status, StatusValue, StatusValueTimestamp}, DeadbandType
   {None, Absolute, Percent}, DeadbandValue, SamplingInterval, QueueSize, PublishingInterval.
2. On receipt the bridge creates an OPC UA monitored item on the data item. Each notification is sent to the
   sender's `ResponseExchangeName` as the item's Get event (the same message the Get command responds with).
3. A monitor is identified by **(sender's `CommandExchangeName`, monitor name)**. A Monitor command with an
   existing key updates that monitor, including replacing its reply-to `BusConfig`. Monitor names are one
   namespace per sender: reusing a name on a different data item moves the monitor to the new item.
4. A generic `DeleteMonitorCommand(MonitorName)` deletes the monitor with the sender's key.
5. Monitors are re-established on every new OPC UA session (server reboot, network loss, bridge start).
6. Monitors are persisted to disk and survive bridge restarts.
7. Each Monitor and Delete command is answered with a `MonitorStatusEvent`. After every rebuild of monitors
   on a new OPC UA session, every non-suspended monitor's sender receives a `MonitorStatusEvent`. No other
   status is sent; `MonitorStatusEvent` is never published on the event exchange.
8. A monitor whose reply-to exchange no longer exists is **suspended**, not deleted: its monitored item is set
   to Disabled and it is kept until the same sender re-sends a Monitor command for it (reclaiming it with the
   new reply-to), it is deleted, or an optional expiry removes it.

## Non-goals

- Making Kafka response topics auto-delete. That is a separate sub-project (own spec) in core StatePipes
  transports. This design only requires that a missing reply-to topic can be detected.
- A lease / keep-alive contract for monitors.
- Using the OPC UA SDK's subscription transfer on reconnect. Every new session rebuilds from the registry.
- Publishing monitor status to dashboards on the event exchange.

## Approach

The persisted registry is the single source of truth. Every new OPC UA session rebuilds all monitors from it,
so bridge start, server reboot and network loss share one code path, matching how the bridge already rebuilds
its catalog on each connect. Commands update the registry first, then apply to the live session if there is
one. The bridge keeps one OPC UA subscription per distinct PublishingInterval, with monitored items grouped
under them (one subscription per monitor would hit servers' per-session subscription limits).

## Message contracts

### Fixed types

Compiled into the bridge in namespace `StatePipes.OpcUaBridge.Messages`, described by reflection
(`TypeSerializationConverter.CreateFromType`) and added to `SelfDescriptionBuilder.StatePipesMessageTypes`.
The self-description format supports enums (`TypeToTypeSerializationConverter` emits `EnumValues`).

| Type | Members |
|---|---|
| enum `MonitoringModeKind` | `Disabled`, `Reporting` |
| enum `DataChangeTriggerKind` | `Status`, `StatusValue`, `StatusValueTimestamp` |
| enum `DeadbandTypeKind` | `None`, `Absolute`, `Percent` |
| enum `MonitorState` | `Active`, `Pending`, `Failed`, `Suspended`, `Deleted` |
| command `DeleteMonitorCommand` | `string MonitorName` |
| event `MonitorStatusEvent` | `string MonitorName`, `string NodeId`, `MonitorState State`, `uint StatusCode`, `string Status`, `double RevisedSamplingInterval`, `uint RevisedQueueSize`, `double RevisedPublishingInterval` |

`MonitorStatusEvent.NodeId` is empty when the node is unknown (see Error handling). Revised values are 0 when
the monitor is not Active.

### Per-item Monitor command

Named like the Get command with a `Monitor_` prefix in place of `Get_`:
`{exchange}.Monitor_ns2_s_Machine_Motor_Speed_Command`. Described by hand in `SelfDescriptionBuilder` with
properties:

| Property | Type |
|---|---|
| `MonitorName` | `string` |
| `MonitoringMode` | `MonitoringModeKind` |
| `DataChangeTrigger` | `DataChangeTriggerKind` |
| `DeadbandType` | `DeadbandTypeKind` |
| `DeadbandValue` | `double` |
| `SamplingInterval` | `double` (ms) |
| `QueueSize` | `uint` |
| `PublishingInterval` | `double` (ms) |

Enum properties reference the fixed enum types by full name.

`MessageNames` gains `MonitorCommandTypeName` / `MonitorCommandTypeFullName`. `OpcUaNaming`'s name budget is
reduced by 4 characters (`Monitor_` is 4 longer than `Get_`) so the Monitor command's full name still fits
the 255-byte RabbitMQ routing-key limit; Get and Monitor names for a node always share the same base, so
collision suffixes and hashes apply identically to both.

### Responses

- **Notification:** the item's Get event body (built by the existing `CreateGetEventBody`), routing key
  `item.EventTypeFullName`, sent to the monitor's stored reply-to with the stored raw reply-to header.
- **`MonitorStatusEvent`:** sent as a response to the sender of a Monitor or Delete command, and to each
  non-suspended monitor's stored reply-to after a rebuild.

### Self-description size

Each item now contributes three messages instead of two (roughly +1 KB per item). The existing over-limit
warning in `OpcUaBridgeService.SetCatalog` covers this; `OPCUA_NODE_FILTER` remains the mitigation.

## Components

### `MonitorDefinition` (record)

What a client asked for — the persisted unit.

- Key: `SenderCommandExchange` (the reply-to's `CommandExchangeName`) + `MonitorName`.
- `NodeIdText` (stable across rediscovery and naming changes; the item is looked up from it).
- The eight parameters.
- `ReplyToRaw` (the raw reply-to header, sent back unchanged) and `ReplyTo` (the `BusConfig` parsed from it,
  not persisted separately).
- `SuspendedSince` (persisted, null when not suspended).
- Runtime only (not persisted): `State`, last status code.

### `MonitorRegistry`

Thread-safe dictionary keyed by (`SenderCommandExchange`, `MonitorName`).

- `Upsert`, `Remove`, `Suspend(key)`, `GetAll`, `RemoveExpired`.
- Persists to `MONITOR_STORE_PATH` (default `Config/monitors.json`) after each change by writing a temp file
  and renaming over the target. Loads on startup.
- Expiry: `MONITOR_SUSPENDED_EXPIRY_HOURS` (default: never). A suspended monitor older than this is removed.
- No OPC UA or broker I/O, so it is unit-testable in isolation.

### `OpcUaMonitorManager`

The OPC UA side, created per session by `OpcUaClient`.

- One `Subscription` per distinct `PublishingInterval`, holding that interval's `MonitoredItem`s.
- `ApplyAsync(definition)` → creates or modifies the item. If the node or the publishing interval changed,
  the item is removed and recreated in the right subscription. Returns status code and revised values.
- `ApplyAsync(definitions)` (batch, used by rebuild) → one `CreateMonitoredItems` call per subscription.
- `RemoveAsync(key)`, `DisableAsync(key)` (sets MonitoringMode Disabled, used by suspension).
- Empty subscriptions are deleted.
- Raises `Notified(key, DataValue)` for every data change notification.
- Always attaches a `DataChangeFilter` built from `DataChangeTrigger`, `DeadbandType` and `DeadbandValue`
  (`DeadbandValue` sent as 0 when `DeadbandType` is `None`), so the server never falls back to its default.

### Changes to existing components

- **`OpcUaBridgeService`**
  - `ConsumeCommand` additionally routes Monitor commands (by catalog, or by `Monitor_` shape when not in the
    catalog) and `DeleteMonitorCommand`, enqueuing `MonitorWork`.
  - Hosts the single-reader `Channel<MonitorWork>` worker.
  - Turns `Notified` into Get event responses.
  - Opens a **second broker channel used only for notifications**, so a RabbitMQ 404 on a vanished response
    exchange closes that channel, not the main channel that consumes commands and publishes heartbeats. The
    notification channel is reopened on demand.
- **`ServiceWorker`** — after `SetCatalog`, enqueues a `Rebuild` for the new session.
- **`SelfDescriptionBuilder`** — describes the per-item Monitor command and the fixed types.
- **`OpcUaNaming` / `MessageNames`** — Monitor command names and the reduced budget.
- **`BridgeSettings`** — `MONITOR_STORE_PATH`, `MONITOR_SUSPENDED_EXPIRY_HOURS`.
- **`README.md`** — new commands/events, settings, `Config/` must be a mounted volume to keep monitors across
  container re-creation, Kafka auto-create note.

## Data flow

### Serialization

All monitor mutations — Monitor and Delete commands, rebuilds, suspensions — go through one
`Channel<MonitorWork>` with a single reader. The transport's receive thread and the OPC UA notification thread
only enqueue. A command can therefore never race a rebuild or touch a subscription being torn down.

### Monitor command

1. `ConsumeCommand`: identify the item from the routing key, deserialize body and reply-to, apply the
   `SendResponse` guards (same broker, same `ClientCertPath`; otherwise log and drop), enqueue.
2. Worker validates (see Error handling). On failure: respond `Failed` + `BadInvalidArgument`; nothing changes.
3. `registry.Upsert` — replaces reply-to, moves the monitor if the key exists on another node, clears
   suspension — and persist.
4. If the session is connected: `ApplyAsync` → `Active` or `Failed`. Otherwise `Pending`.
5. Respond `MonitorStatusEvent` to the new reply-to.

### Delete command

`registry.Remove` + persist, `RemoveAsync` if connected, respond `Deleted` — or `Failed` + `BadNotFound` if the
key does not exist. Works before discovery and for suspended monitors (status goes to the Delete's reply-to).

### Notification

`Notified(key, value)` → look up the definition → `CreateGetEventBody(item, value)` → send on the notification
channel to the definition's reply-to with its `ReplyToRaw`.

### Rebuild (every new OPC UA session)

`ServiceWorker` connects and discovers → `SetCatalog` → enqueue `Rebuild`. The worker:

1. Removes expired suspended monitors.
2. Creates an `OpcUaMonitorManager` for the session.
3. For each non-suspended definition: if its NodeId is not in the new catalog, mark `Failed` +
   `BadNodeIdUnknown`; otherwise include it in the batch apply.
4. Sends each non-suspended monitor's sender a `MonitorStatusEvent`.

Suspended monitors are not created as active and receive no status.

### Suspension

`ISimpleConnectionChannel` sends are fire-and-forget on both brokers, so a failed send gives no signal. Instead
the channel gains `Task<bool> BusExistsAsync(string exchangeName, CancellationToken ct)`: a passive
exchange declare on a temporary RabbitMQ channel, a topic metadata lookup on Kafka.

- The coordinator checks every distinct reply-to of non-suspended monitors every 30 s, and immediately when the
  notification channel is found closed (RabbitMQ closes it on a 404 from a vanished exchange).
- A reply-to whose bus does not exist → `Suspend(key)` for every monitor using it: `DisableAsync(key)`,
  `registry.Suspend(key)` (sets `SuspendedSince`), persist.
- **Kafka:** requires the broker to have `auto.create.topics.enable=false`; otherwise a notification produced
  before the next check recreates the topic and the monitor is never suspended (documented limitation until the
  Kafka auto-delete sub-project lands).
- Reclaim: the same sender re-sends a Monitor command with the same name → normal Monitor flow.

## Persistence

```json
{
  "version": 1,
  "monitors": [
    {
      "senderCommandExchange": "Client1.commands",
      "monitorName": "MotorSpeed",
      "nodeId": "ns=2;s=Machine.Motor.Speed",
      "monitoringMode": "Reporting",
      "dataChangeTrigger": "StatusValue",
      "deadbandType": "None",
      "deadbandValue": 0,
      "samplingInterval": 1000,
      "queueSize": 1,
      "publishingInterval": 1000,
      "replyTo": "<raw reply-to header JSON>",
      "suspendedSince": null
    }
  ]
}
```

- **Load:** all monitors start `Pending`. Entries whose reply-to fails the `SendResponse` guards (the
  bridge's broker or cert changed) are dropped with a warning. Expired suspended entries are removed.
- **Corrupt/unreadable file:** renamed to `monitors.json.corrupt-{UTC timestamp}`, error logged, start empty.
  Never silently overwritten.
- **Write failure:** error logged, in-memory registry stays authoritative, next change retries. The bridge
  keeps running.
- **Expiry** checked on each rebuild and hourly.
- **Docker:** `Config/` is pre-created owned by `$APP_UID`; it must be a mounted volume for monitors to survive
  container re-creation.

## Error handling

### Validation (before anything is stored; failure → `Failed` + `BadInvalidArgument`, no change)

- Body deserializes and every enum value is defined.
- `MonitorName` non-empty, ≤ 256 characters.
- `SamplingInterval` ≥ 0 (0 = server's fastest); `PublishingInterval` > 0; `QueueSize` ≥ 1.
- `DeadbandValue` ≥ 0, and ≤ 100 when `Percent`; ignored when `None`.

### Monitor command not in the catalog

`ConsumeCommand` recognises a Monitor command by routing key shape (`{exchange}.Monitor_…_Command`) without the
catalog, reads `MonitorName` from the body and the reply-to from the header. Nothing is stored; the client must
re-send.

- **Before the first discovery** (no OPC UA session has completed yet): `Failed` +
  `BadWaitingForInitialData`, empty `NodeId`.
- **After discovery, unknown node:** `Failed` + `BadNodeIdUnknown`, empty `NodeId`.

Between sessions the previous catalog stays in effect; a Monitor command for a known node while disconnected is
stored `Pending`.

### OPC UA errors

- **Per-item error** from Create/ModifyMonitoredItems (`BadFilterNotAllowed`, `BadNodeIdUnknown`,
  `BadTooManyMonitoredItems`, …): the monitor is kept as `Failed`, the sender gets that status code, and it is
  retried on each rebuild.
- **Call- or subscription-level error** (e.g. session drops mid-apply): affected monitors become `Pending`;
  the reconnect loop rebuilds them.
- A rebuild failure is reported only through the post-rebuild `MonitorStatusEvent` and the bridge log.

### Broker errors

- Broker unavailable during a notification: dropped and logged verbose; no queueing — the next data change
  carries the current value.
- Response send guards fail: logged and dropped, as for Get today.

## Testing

### Unit tests (`StatePipes.Test/OpcUaBridge`)

- `MonitorRegistry`: upsert/replace/move between nodes, per-sender key scoping, suspend and reclaim, expiry,
  persist/load round-trip, atomic write, corrupt-file rename and empty start, guard-failing entries dropped on
  load.
- Command validation: every rule above, including both not-in-catalog cases and their status codes.
- `OpcUaNaming`: Monitor names fit 255 bytes, collisions resolved identically to Get names, Get/Monitor pairing.
- `SelfDescriptionBuilder`: Monitor command and fixed/enum types described; Explorer's
  `TypeSerializationToTypeConverter` round-trips them.

### Integration tests (in-process `PlcServer` from the `OpcUaServer` project on a test port; fake broker channel)

- Create → Get events on value change, `MonitorStatusEvent` `Active` with revised values.
- Absolute deadband suppresses small changes; Percent on a non-analog node → `Failed` + `BadFilterNotAllowed`.
- Update same name with a new PublishingInterval → item moves subscription, notifications continue.
- Delete → notifications stop; unknown name → `BadNotFound`.
- Monitor before first discovery → `BadWaitingForInitialData`.
- Server reboot: stop/restart `OpcUaServer` → monitors return, status per monitor, notifications resume.
- Bridge restart: new bridge with the same `monitors.json` → monitors return.
- Suspension: fake channel reports the reply-to bus missing → monitor suspended, Gets still served;
  re-send → reclaimed.

### Manual end-to-end

`opc-plc` in Docker against both brokers using the existing harness: RabbitMQ client disconnect → suspension
with the main channel unaffected; Kafka broker with `auto.create.topics.enable=false` → suspension.
`BusExistsAsync` on both real brokers is verified only here.

## Known limitations

- On Kafka, suspension depends on broker auto-create being disabled, and a crashed client's topic is never
  removed until the Kafka auto-delete sub-project is done.
- Status is sent only as a command response or after a rebuild; a monitor failing at any other time is visible
  only in the bridge log.
