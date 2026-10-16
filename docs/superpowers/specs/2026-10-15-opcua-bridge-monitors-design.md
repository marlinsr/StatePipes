# OPC UA Bridge Monitors — Design

**Date:** 2026-10-15 (revision 2 the same day: monitors identified by data item; per-item Delete and status)
**Branch:** `opcua-bridge`
**Component:** `StatePipes.OpcUaBridge`

## Goal

Let StatePipes clients subscribe to OPC UA data changes through the bridge instead of polling Get commands.
For every readable data item the bridge already exposes as a Get command / Get event pair, it also exposes a
Monitor command, a Delete command and a MonitorStatus event. A client sends the Monitor command to set up an
OPC UA monitored item on that data item; every data change the OPC UA server reports for it is sent back to that
client as the item's existing Get event. Monitors survive both OPC UA server reboots and bridge restarts, and a
client can update or delete its monitor on each data item.

## Requirements (as agreed)

1. Every readable data item gets a runtime Monitor command whose body defines: monitoring mode
   {Disabled, Reporting}, DataChangeTrigger {Status, StatusValue, StatusValueTimestamp}, DeadbandType
   {None, Absolute, Percent}, DeadbandValue, SamplingInterval, QueueSize, PublishingInterval.
2. On receipt the bridge creates an OPC UA monitored item on the data item. Each notification is sent to the
   sender's `ResponseExchangeName` as the item's Get event (the same message the Get command responds with).
3. A monitor is identified by **(sender's `CommandExchangeName`, the data item's NodeId)**: each sender has at
   most one monitor per data item. A Monitor command for an item the sender already monitors updates that
   monitor, including replacing its reply-to `BusConfig`.
4. Every data item gets a runtime Delete command with an empty body, which deletes the sender's monitor on that
   item.
5. Monitors are re-established on every new OPC UA session (server reboot, network loss, bridge start).
6. Monitors are persisted to disk and survive bridge restarts.
7. Each Monitor and Delete command is answered with that data item's MonitorStatus event. After every rebuild
   of monitors on a new OPC UA session, every non-suspended monitor's sender receives the item's MonitorStatus
   event. No other status is sent; MonitorStatus events are never published on the event exchange.
8. A monitor whose reply-to exchange no longer exists is **suspended**, not deleted: its monitored item is set
   to Disabled and it is kept until the same sender re-sends the Monitor command (reclaiming it with the new
   reply-to), it is deleted, or an optional expiry removes it.
9. Every per-item message (Get, Monitor, Delete, MonitorStatus) is in the data item's namespace, so
   StatePipes.Explorer shows them together.

## Non-goals

- Making Kafka response topics auto-delete. That is a separate sub-project (own spec) in core StatePipes
  transports. This design only requires that a missing reply-to topic can be detected.
- A lease / keep-alive contract for monitors.
- Using the OPC UA SDK's subscription transfer on reconnect. Every new session rebuilds from the registry.
- Publishing monitor status to dashboards on the event exchange.
- Migrating revision-1 (`version: 1`) monitor files; the branch had not shipped.

## Approach

The persisted registry is the single source of truth. Every new OPC UA session rebuilds all monitors from it,
so bridge start, server reboot and network loss share one code path, matching how the bridge already rebuilds
its catalog on each connect. Commands update the registry first, then apply to the live session if there is
one. The bridge keeps one OPC UA subscription per distinct PublishingInterval, with monitored items grouped
under them (one subscription per monitor would hit servers' per-session subscription limits).

## Message contracts

### Fixed enums

Compiled into the bridge in namespace `StatePipes.OpcUaBridge.Messages`. They are not listed at the top level of
the self description (clients expect only messages there); each message that uses one embeds its description.

| Enum | Members |
|---|---|
| `MonitoringModeKind` | `Disabled`, `Reporting` |
| `DataChangeTriggerKind` | `Status`, `StatusValue`, `StatusValueTimestamp` |
| `DeadbandTypeKind` | `None`, `Absolute`, `Percent` |
| `MonitorState` | `Active`, `Pending`, `Failed`, `Suspended`, `Deleted` |

### Per-item messages

All described by hand in `SelfDescriptionBuilder`, in the item's namespace, sharing the Get name's base
(`{exchange}.{prefix}ns2_s_Machine_Motor_Speed{suffix}`), so collision suffixes and hashes apply identically:

| Message | Name | Properties |
|---|---|---|
| Get command | `Get_…_Command` | none (unchanged) |
| Get event | `Get_…_Event` | unchanged |
| Monitor command | `Monitor_…_Command` | `MonitoringMode: MonitoringModeKind`, `DataChangeTrigger: DataChangeTriggerKind`, `DeadbandType: DeadbandTypeKind`, `DeadbandValue: double`, `SamplingInterval: double` (ms), `QueueSize: uint`, `PublishingInterval: double` (ms) |
| Delete command | `Delete_…_Command` | none |
| MonitorStatus event | `MonitorStatus_…_Event` | `NodeId: string`, `State: MonitorState`, `StatusCode: uint`, `Status: string`, `RevisedSamplingInterval: double`, `RevisedQueueSize: uint`, `RevisedPublishingInterval: double` |

`NodeId` is empty when the node is unknown (see Error handling). Revised values are 0 unless `Active`.

`MessageNames` gains `MonitorCommand…`, `DeleteCommand…` and `MonitorStatusEvent…` type names. The longest
prefix/suffix pair is `MonitorStatus_` + `_Event`, 8 characters longer than `Get_` + `_Command`, so
`OpcUaNaming`'s name budget is reduced by 8 so every full name fits the 255-byte RabbitMQ routing-key limit.

### Responses

- **Notification:** the item's Get event body (built by the existing `CreateGetEventBody`), routing key
  `item.EventTypeFullName`, sent to the monitor's stored reply-to with the stored raw reply-to header.
- **MonitorStatus event:** sent as a response to the sender of a Monitor or Delete command, and to each
  non-suspended monitor's stored reply-to after a rebuild.

### Self-description size

Each item contributes five messages. Measured size is stated in the README; the existing over-limit warning in
`OpcUaBridgeService.SetCatalog` covers it; `OPCUA_NODE_FILTER` remains the mitigation.

## Components

### `MonitorDefinition` (record)

What a client asked for — the persisted unit.

- Key: `MonitorKey(SenderCommandExchange, NodeIdText)` — the reply-to's `CommandExchangeName` and the item's
  NodeId in standard notation (stable across rediscovery and naming changes; the item is looked up from it).
- The seven parameters.
- `ReplyToRaw` (the raw reply-to header, sent back unchanged) and `ReplyTo` (the `BusConfig` parsed from it,
  not persisted separately).
- `SuspendedSince` (persisted, null when not suspended).

### `MonitorRegistry`

Thread-safe dictionary keyed by `MonitorKey`.

- `Upsert`, `Remove`, `Suspend(key)`, `GetAll`, `RemoveExpired`.
- Persists to `MONITOR_STORE_PATH` (default StatePipes' Config directory,
  `/usr/share/StatePipes/StatePipes.OpcUaBridge/Config/monitors.json` in the container) after each change by
  writing a temp file and renaming over the target. Loads on startup.
- Expiry: `MONITOR_SUSPENDED_EXPIRY_HOURS` (default: never). A suspended monitor older than this is removed.
- No OPC UA or broker I/O, so it is unit-testable in isolation.

### `OpcUaMonitorManager`

The OPC UA side, created per session by `OpcUaClient`.

- One `Subscription` per distinct `PublishingInterval`, holding that interval's `MonitoredItem`s.
- `ApplyAsync(definition)` → creates or modifies the item. If the publishing interval changed, the item is
  removed and recreated in the right subscription. Returns status code and revised values.
- `ApplyAllAsync(definitions)` (batch, used by rebuild) → one `CreateMonitoredItems` call per subscription.
- `RemoveAsync(key)`, `DisableAsync(key)` (sets MonitoringMode Disabled, used by suspension).
- Empty subscriptions are deleted.
- Reports `(key, DataValue)` for every data change notification.
- Always attaches a `DataChangeFilter` built from `DataChangeTrigger`, `DeadbandType` and `DeadbandValue`
  (`DeadbandValue` sent as 0 when `DeadbandType` is `None`), so the server never falls back to its default.

### Changes to existing components

- **`OpcUaBridgeService`**
  - `ConsumeCommand` additionally routes Monitor and Delete commands (by catalog, or by routing-key shape when
    not in the catalog) to the coordinator.
  - Turns notifications into Get event responses.
  - Opens a **second broker channel used only for monitor responses**, so a RabbitMQ 404 on a vanished response
    exchange closes that channel, not the main channel that consumes commands and publishes heartbeats.
- **`ServiceWorker`** — after `SetCatalog`, starts the coordinator's rebuild for the new session.
- **`SelfDescriptionBuilder`** — describes the per-item Monitor, Delete and MonitorStatus messages.
- **`OpcUaNaming` / `MessageNames`** — the per-item names and the reduced budget.
- **`BridgeSettings`** — `MONITOR_STORE_PATH`, `MONITOR_SUSPENDED_EXPIRY_HOURS`.
- **`README.md`** — new commands/events, settings, Config must be a mounted volume, Kafka auto-create note,
  re-send after reconnect.

## Data flow

### Serialization

All monitor mutations — Monitor and Delete commands, rebuilds, suspensions, expiry — go through one queue with
a single reader. The transport's receive thread and the OPC UA notification thread only enqueue (or, for
notifications, read). A command can therefore never race a rebuild or touch a subscription being torn down.

### Monitor command

1. `ConsumeCommand`: identify the item from the routing key, deserialize body and reply-to, apply the
   `SendResponse` guards (same broker, same `ClientCertPath`; otherwise log and drop).
2. Validate (see Error handling). On failure: respond `Failed` + `BadInvalidArgument`; nothing changes.
3. `registry.Upsert` — replaces parameters and reply-to, clears suspension — and persist.
4. If the session is connected: `ApplyAsync` → `Active` or `Failed`. Otherwise `Pending`.
5. Respond with the item's MonitorStatus event to the new reply-to.

### Delete command

`registry.Remove` + persist, `RemoveAsync` if connected, respond `Deleted` — or `Failed` + `BadNotFound` if the
sender has no monitor on that item. Works for suspended monitors (status goes to the Delete's reply-to).

### Notification

`(key, value)` → look up the definition → `CreateGetEventBody(item, value)` → send on the notification channel
to the definition's reply-to with its `ReplyToRaw`.

### Rebuild (every new OPC UA session)

`ServiceWorker` connects and discovers → `SetCatalog` → rebuild. The coordinator:

1. Removes expired suspended monitors.
2. Takes the new session's `OpcUaMonitorManager`.
3. For each non-suspended definition: if its NodeId is not in the new catalog, mark `Failed` +
   `BadNodeIdUnknown`; otherwise include it in the batch apply.
4. Sends each non-suspended monitor's sender the item's MonitorStatus event.

Suspended monitors are not created as active and receive no status.

### Suspension

`ISimpleConnectionChannel` sends are fire-and-forget on both brokers, so a failed send gives no signal. Instead
the channel gains `Task<bool> BusExistsAsync(string exchangeName, CancellationToken ct)`: a passive
exchange declare on a temporary RabbitMQ channel, a topic metadata lookup on Kafka.

- The coordinator checks every distinct reply-to of non-suspended monitors every 30 s, and immediately when the
  notification channel is found closed (RabbitMQ closes it on a 404 from a vanished exchange).
- A reply-to whose bus does not exist → for every monitor using it: `DisableAsync(key)`, `registry.Suspend(key)`
  (sets `SuspendedSince`), persist.
- **Kafka:** requires the broker to have `auto.create.topics.enable=false`; otherwise a notification produced
  before the next check recreates the topic and the monitor is never suspended (documented limitation until the
  Kafka auto-delete sub-project lands).
- Reclaim: the same sender re-sends the item's Monitor command → normal Monitor flow.

## Persistence

```json
{
  "version": 2,
  "monitors": [
    {
      "senderCommandExchange": "Client1.commands",
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
- **Corrupt/unreadable file, or any version other than 2:** renamed to `monitors.json.corrupt-{UTC timestamp}`,
  error logged, start empty. Never silently overwritten.
- **Write failure:** error logged, in-memory registry stays authoritative, next change retries. The bridge
  keeps running.
- **Expiry** checked on each rebuild and hourly.
- **Docker:** the Config directory is pre-created owned by `$APP_UID`; it must be a mounted volume for monitors
  to survive container re-creation.

## Error handling

### Validation (before anything is stored; failure → `Failed` + `BadInvalidArgument`, no change)

- Body is a JSON object, every field is present, and every enum value is defined (as a name or a number).
- `SamplingInterval` ≥ 0 (0 = server's fastest); `PublishingInterval` > 0; `QueueSize` ≥ 1.
- `DeadbandValue` ≥ 0, and ≤ 100 when `Percent`; ignored (stored as 0) when `None`.

### Monitor or Delete command not in the catalog

`ConsumeCommand` recognises a per-item Monitor or Delete command by routing key shape
(`{namespace}.Monitor_…_Command`, `{namespace}.Delete_…_Command`) without the catalog, and answers on the
MonitorStatus event whose name it derives from the routing key (prefix → `MonitorStatus_`, suffix → `_Event`).
A Monitor command stores nothing; the client must re-send. A Delete command still deletes the sender's stored
monitor on the item those names belong to (the bridge remembers the names of every item it has listed), so a
monitor on an item that has left the server can be removed; it is answered `Deleted` with that NodeId. Only when
there is no such monitor does it get the status below.

- **Before the first discovery** (no OPC UA session has completed yet): `Failed` +
  `BadWaitingForInitialData`, empty `NodeId`.
- **After discovery, unknown node:** `Failed` + `BadNodeIdUnknown`, empty `NodeId`.

Between sessions the previous catalog stays in effect; a Monitor command for a known node while disconnected is
stored `Pending`.

### OPC UA errors

- **Per-item error** from Create/ModifyMonitoredItems (`BadDeadbandFilterInvalid`, `BadFilterNotAllowed`,
  `BadNodeIdUnknown`, `BadTooManyMonitoredItems`, …): the monitor is kept as `Failed`, the sender gets that
  status code, and it is retried on each rebuild.
- **Call- or subscription-level error:** while the session is still connected (too many subscriptions, a
  timeout) the monitor is `Failed` with the server's code and retried on each rebuild; when the session is gone
  it is `Pending` and the reconnect loop rebuilds it.
- A rebuild failure is reported only through the post-rebuild MonitorStatus event and the bridge log.

### Broker errors

- Broker unavailable during a notification: dropped and logged verbose; no queueing — the next data change
  carries the current value.
- Response send guards fail: logged and dropped, as for Get today.

## Testing

### Unit tests (`StatePipes.Test/OpcUaBridge`)

- `MonitorRegistry`: upsert/replace, per-sender key scoping, suspend and reclaim, expiry, persist/load
  round-trip, atomic write, corrupt-file and version-1 rename with empty start, guard-failing entries dropped on
  load.
- Command validation: every rule above, including both not-in-catalog cases (Monitor and Delete) and their
  status codes and derived status event names.
- `OpcUaNaming`: all per-item names fit 255 bytes, collisions resolved identically to Get names.
- `SelfDescriptionBuilder`: Monitor, Delete and MonitorStatus are described in the item's namespace and
  Explorer's `TypeSerializationJsonHelper` emits types for them.

### Integration tests (in-process `PlcServer` from the `OpcUaServer` project on a test port; fake broker channel)

- Create → Get events on value change, MonitorStatus `Active` with revised values.
- Absolute deadband suppresses small changes; Percent on a non-analog node → `Failed` with the server's code.
- Update with a new PublishingInterval → item moves subscription, notifications continue.
- Delete → notifications stop; no monitor → `BadNotFound`.
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

- A short client reconnect can suspend its monitors: the auto-delete response exchange vanishes while the client
  is disconnected, and a reply-to check in that gap suspends them. Suspended monitors are not reactivated
  automatically and no status is sent (decided 2026-10-15); clients must re-send their Monitor commands after
  every reconnect.
- On Kafka, suspension depends on broker auto-create being disabled, and a crashed client's topic is never
  removed until the Kafka auto-delete sub-project is done.
- Status is sent only as a command response or after a rebuild; a monitor failing at any other time is visible
  only in the bridge log.
