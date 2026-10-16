# StatePipes.OpcUaBridge

A docker container that presents an OPC UA server as a StatePipes service on a RabbitMQ or Kafka broker.

On startup, OpcUaBridge connects to the OPC UA server and finds every variable the session can read. For each one it exposes a **Get command**, a **Monitor command**, a **Delete command** and a **MonitorStatus event**. When it receives a Get command, it reads the variable and sends a **Get event** containing the value back to the sender as a response. A Monitor command sets up an OPC UA monitored item instead, and every data change the server reports goes back to the sender as the same Get event. The bridge also answers `GetSelfDescriptionCommand` and publishes `HeartbeatEvent`, so StatePipes.Explorer and generated proxies treat it like any other service.

## Naming

Every message of a data item is in the namespace given by the bridge's `EXCHANGE`, so StatePipes.Explorer shows them together. The type name is the whole NodeId: its namespace index, the letter of its identifier type (`i`, `s`, `g` or `b`, as in NodeId notation) and its identifier, with dots replaced by underscores, prefixed with `Get_`, `Monitor_`, `Delete_` or `MonitorStatus_` and suffixed with `_Command` or `_Event`. With `EXCHANGE=Line1Plc`:

| OPC UA node | Get command / event | Monitor command | Delete command | MonitorStatus event |
|---|---|---|---|---|
| `ns=2;s=Machine.Motor.Speed` | `Get_ns2_s_Machine_Motor_Speed_Command` / `_Event` | `Monitor_ns2_s_Machine_Motor_Speed_Command` | `Delete_ns2_s_Machine_Motor_Speed_Command` | `MonitorStatus_ns2_s_Machine_Motor_Speed_Event` |
| `ns=2;s=Speed` | `Get_ns2_s_Speed_Command` / `_Event` | `Monitor_ns2_s_Speed_Command` | `Delete_ns2_s_Speed_Command` | `MonitorStatus_ns2_s_Speed_Event` |
| `ns=3;i=1001` | `Get_ns3_i_1001_Command` / `_Event` | `Monitor_ns3_i_1001_Command` | `Delete_ns3_i_1001_Command` | `MonitorStatus_ns3_i_1001_Event` |

- Any other character that can't appear in a type name becomes `_`, in the type name and in each segment of the exchange name.
- If two nodes still end up with the same type name (for example `A-B` and `A_B`), the later one in NodeId order gets a `_2` suffix (or the next number not already used by another node), and a warning is logged. The item's other messages get the same suffix.
- The full name is the RabbitMQ routing key, which is capped at 255 bytes. Type names too long for that are shortened and end with a hash of the NodeId.

## Get event

```json
{
  "NodeId": "ns=2;s=Line1.Temperature",
  "Value": 21.5,
  "StatusCode": 0,
  "Status": "Good",
  "SourceTimestamp": "2026-10-02T15:24:40.35Z",
  "ServerTimestamp": "2026-10-02T15:24:44.87Z"
}
```

`Value` keeps the variable's natural type for scalars and one-dimensional arrays of Boolean, the integer types, Float, Double, String, DateTime, Guid and ByteString. Enumerations are sent as `int`. Everything else is sent as a string with the value's text or JSON form. This includes structures, NodeIds, LocalizedText, matrices, and variables whose type or rank isn't fixed.

When a read fails, `Value` is left out and `StatusCode`/`Status` explain why. For example, `BadNotConnected` means the OPC UA server is unreachable.

## Monitors

A variable's Monitor command asks the bridge to watch it:

```json
{
  "MonitoringMode": "Reporting",
  "DataChangeTrigger": "StatusValue",
  "DeadbandType": "Absolute",
  "DeadbandValue": 0.5,
  "SamplingInterval": 100,
  "QueueSize": 10,
  "PublishingInterval": 1000
}
```

| Field | Values | Meaning |
|---|---|---|
| `MonitoringMode` | `Disabled`, `Reporting` | `Disabled` keeps the monitored item on the server but silent. |
| `DataChangeTrigger` | `Status`, `StatusValue`, `StatusValueTimestamp` | What counts as a change. |
| `DeadbandType` | `None`, `Absolute`, `Percent` | How small a change is ignored. `Percent` needs an AnalogItem with an EURange. |
| `DeadbandValue` | at least 0 (at most 100 for `Percent`) | Ignored when `DeadbandType` is `None`. |
| `SamplingInterval` | ms, at least 0 | 0 asks for the server's fastest rate. |
| `QueueSize` | at least 1 | Values the server queues between publishes; the oldest is discarded. |
| `PublishingInterval` | ms, more than 0 | How often the server sends changes. Monitors with the same interval share one OPC UA subscription. |

Enums can be sent as names or numbers. The server may revise the intervals and queue size; the revised values come back in the status.

- **Identity.** You have at most one monitor per variable. Sending a variable's Monitor command again updates your monitor on it, and its responses go to the reply-to of the latest command. A restarted client takes its monitors back by sending its Monitor commands again. Other clients' monitors on the same variable are separate.
- **Data changes** arrive as the variable's Get event, sent to you as a response.
- **Delete.** A variable's Delete command (empty body) deletes your monitor on it.
- **Persistence.** Monitors are saved to `MONITOR_STORE_PATH` and rebuilt on every new OPC UA session, so they survive both a PLC reboot and a bridge restart.
- **Suspension.** When your response exchange disappears (your client disconnected), your monitors are suspended. The bridge stops sending and keeps them until you send their Monitor commands again, delete them, or `MONITOR_SUSPENDED_EXPIRY_HOURS` passes. The bridge checks every 30 seconds.
- **Re-send your Monitor commands after every reconnect.** A client's response exchange is deleted whenever its broker connection drops, even briefly. If a check happens during that gap, your monitors are suspended, and they stay suspended after you reconnect, with no status sent. Sending your Monitor commands again after each reconnect reclaims them; for monitors that are still active, it is a harmless update.

### Monitor status

Every Monitor and Delete command is answered with the variable's MonitorStatus event. Every monitor that is not suspended also gets one each time monitors are rebuilt on a new OPC UA session:

```json
{
  "NodeId": "ns=2;s=Machine.Motor.Speed",
  "State": "Active",
  "StatusCode": 0,
  "Status": "Good",
  "RevisedSamplingInterval": 100,
  "RevisedQueueSize": 10,
  "RevisedPublishingInterval": 1000
}
```

`State` is `Active`, `Pending` (saved while the OPC UA server is unreachable), `Failed`, or `Deleted`. Revised values are 0 unless `Active`.

| Status | Meaning |
|---|---|
| `BadInvalidArgument` | A field is missing or out of range. Nothing was saved. |
| `BadWaitingForInitialData` | The bridge hasn't discovered the server yet. Nothing was saved or deleted; send the command again. `NodeId` is empty. |
| `BadNodeIdUnknown` | The variable is not on the server, or no longer is. |
| `BadNotFound` | Delete: you have no monitor on that variable. |
| `BadNotConnected` | `Pending`: saved, and applied when the server is back. |
| `BadDeadbandFilterInvalid` / `BadFilterNotAllowed` | The server refused the deadband, for example `Percent` on a variable that is not an AnalogItem. The monitor is kept and retried on every reconnect. |

## Configuration

Each setting is read from an environment variable. The equivalent `--NAME=value` command line argument works too.

### StatePipes side (same names as BrokerProxy)

| Variable | Required | Description |
|---|---|---|
| `BROKER` | yes | Broker URI. `ssl://host:9093` selects Kafka; anything else (e.g. `amqps://amqp09-broker/Production`) selects RabbitMQ. |
| `EXCHANGE` | yes | Service name the bridge appears as. Commands arrive on `<EXCHANGE>.commands`. |
| `CERTPATH` | yes | Client certificate (`.p12`) for the broker. |
| `PWPATH` | yes | File containing the certificate password. |

`CERTPATH` and `PWPATH` are file names, looked up first in `/usr/share/StatePipes/StatePipes.OpcUaBridge/Certs`. Like every StatePipes service, the bridge only answers clients whose `CERTPATH` is spelled the same way. Plain file names in the Certs directory are the simplest choice.

### OPC UA side

| Variable | Default | Description |
|---|---|---|
| `OPCUA_ENDPOINT` | *(required)* | Server endpoint, e.g. `opc.tcp://plc:4840`. |
| `OPCUA_USE_SECURITY` | `true` | Pick the most secure endpoint the server offers. Set to `false` to use `None`. |
| `OPCUA_USERNAME` | *(anonymous)* | User name for a username/password identity. |
| `OPCUA_PASSWORD` / `OPCUA_PASSWORD_PATH` | | Password, or a file containing it (for docker secrets). |
| `OPCUA_AUTO_ACCEPT_UNTRUSTED` | `false` | Trust any server certificate. Convenient for testing; when it's off, untrusted certificates land in `/pki/rejected/certs`, and you trust one by moving it to `/pki/trusted/certs`. |
| `OPCUA_PKI_PATH` | `/pki` | Certificate stores. The bridge creates its own application certificate in `/pki/own` on first start; the server may need to trust it. |
| `OPCUA_APPLICATION_NAME` | `StatePipes.OpcUaBridge` | Application and session name shown to the server. |
| `OPCUA_INCLUDE_NS0` | `false` | Also expose namespace 0 and the `http://opcfoundation.org/UA/Diagnostics` namespace (the server's own diagnostics). |
| `OPCUA_NODE_FILTER` | | Regex matched against the NodeId text. When set, only matching nodes are exposed. |
| `OPCUA_RECONNECT_SECONDS` | `5` | Delay before reconnecting after the session is lost. |

### Monitors

| Variable | Default | Description |
|---|---|---|
| `MONITOR_STORE_PATH` | `/usr/share/StatePipes/StatePipes.OpcUaBridge/Config/monitors.json` | Where monitors are saved. Mount the directory as a volume to keep monitors when the container is re-created. An unreadable file, or one written by an earlier bridge version, is renamed to `monitors.json.corrupt-<time>` and the bridge starts with no monitors. |
| `MONITOR_SUSPENDED_EXPIRY_HOURS` | *(never)* | Delete monitors that stay suspended this long. |

On Kafka, suspension needs the broker setting `auto.create.topics.enable=false`. Otherwise the next data change recreates a departed client's response topic, and its monitors keep sending to it.

Discovery browses down from the Objects folder. It runs again after every reconnect, so a restarted or reconfigured server is picked up automatically. While the server is unreachable, Get commands are still answered, with a Bad status.

### Large servers

The self description lists all five messages of every variable in **one message**, at roughly 10.7 KB per variable (about 3.3 KB of it for the Get command and event). Brokers cap message size by default: 16 MiB for RabbitMQ 4 and 1 MiB for Kafka. That works out to about 1,500 variables on RabbitMQ and about 95 on Kafka. The bridge logs the size at startup and logs an error when it's over the limit. To stay under it, narrow the exposed nodes with `OPCUA_NODE_FILTER` (e.g. `^ns=3;`) or raise the broker's limit.

## Running

```sh
docker run -d --name opcuabridge \
  -v /path/to/certs:/usr/share/StatePipes/StatePipes.OpcUaBridge/Certs:ro \
  -v /path/to/root_ca.pem:/etc/ssl/certs/root_ca.pem:ro \
  -v /path/to/intermediate_ca.pem:/etc/ssl/certs/intermediate_ca.pem:ro \
  -v opcuabridge-pki:/pki \
  -v opcuabridge-config:/usr/share/StatePipes/StatePipes.OpcUaBridge/Config \
  -e OPCUA_ENDPOINT=opc.tcp://plc:4840 \
  -e BROKER=amqps://amqp09-broker/Production \
  -e EXCHANGE=Line1Plc \
  -e CERTPATH=amqpuser.amqp09-broker.client.p12 \
  -e PWPATH=CommonPassword.txt \
  bigfish88/statepipesopcuabridge:latest
```

The CA certificates let the container trust the broker's TLS certificate, the same way StatePipes.Explorer's container does.

To try it without a PLC, use Microsoft's simulator: `docker run -d -p 50000:50000 mcr.microsoft.com/iotedge/opc-plc --pn=50000 --autoaccept`. Its telemetry nodes are in `ns=3`.

## Design notes

- The bridge doesn't host a `StatePipesService`. Its Get commands only exist at runtime, and a compiled service dispatches on compiled message types. Instead it speaks the StatePipes wire format directly over BrokerProxy's RabbitMQ/Kafka channels, which it compiles as linked source.
- Get commands are queued off the broker's receive thread. A single reader then batches whatever has queued up into one OPC UA Read.
- Every monitor change (commands, rebuilds, suspension, expiry) goes through one queue with a single reader, so a command never races a rebuild.
- Monitor responses use a second broker channel. RabbitMQ closes a channel that publishes to a missing exchange; on its own channel that never interrupts commands or heartbeats, and it triggers an immediate reply-to check.
