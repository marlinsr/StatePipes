# StatePipes.OpcUaBridge

A docker container that presents an OPC UA server as a StatePipes service on a RabbitMQ or Kafka broker.

On startup, OpcUaBridge connects to the OPC UA server and finds every variable the session can read. For each one it exposes a **Get command**, a **Monitor command**, a **Delete command** and a **MonitorStatus event**. When it receives a Get command, it reads the variable and sends a **Get event** containing the value back to the sender as a response. A Monitor command sets up an OPC UA monitored item instead, and every data change the server reports goes back to the sender as the same Get event. Each variable the session may also write gets a **Set command** and a **SetStatus event**: a Set command writes its value, and the SetStatus event tells the sender whether the write succeeded. Each method the session may execute gets an **Exec command** and an **ExecResult event**: an Exec command calls the method with its input arguments, and the ExecResult event returns the output arguments. The bridge also answers `GetSelfDescriptionCommand` and publishes `HeartbeatEvent`, so StatePipes.Explorer and generated proxies treat it like any other service.

## Naming

Every message of a data item is in the namespace given by the bridge's `EXCHANGE`, so StatePipes.Explorer shows them together. The type name is the whole NodeId: its namespace index, the letter of its identifier type (`i`, `s`, `g` or `b`, as in NodeId notation) and its identifier, with dots replaced by underscores, prefixed with `Get_`, `Monitor_`, `Delete_`, `MonitorStatus_`, `Set_` or `SetStatus_` and suffixed with `_Command` or `_Event`. With `EXCHANGE=Line1Plc`:

| OPC UA node | Get command / event | Monitor command | Delete command | MonitorStatus event |
|---|---|---|---|---|
| `ns=2;s=Machine.Motor.Speed` | `Get_ns2_s_Machine_Motor_Speed_Command` / `_Event` | `Monitor_ns2_s_Machine_Motor_Speed_Command` | `Delete_ns2_s_Machine_Motor_Speed_Command` | `MonitorStatus_ns2_s_Machine_Motor_Speed_Event` |
| `ns=2;s=Speed` | `Get_ns2_s_Speed_Command` / `_Event` | `Monitor_ns2_s_Speed_Command` | `Delete_ns2_s_Speed_Command` | `MonitorStatus_ns2_s_Speed_Event` |
| `ns=3;i=1001` | `Get_ns3_i_1001_Command` / `_Event` | `Monitor_ns3_i_1001_Command` | `Delete_ns3_i_1001_Command` | `MonitorStatus_ns3_i_1001_Event` |

A writable variable's Set command and SetStatus event follow the same pattern: `Set_ns2_s_Machine_Motor_Speed_Command` and `SetStatus_ns2_s_Machine_Motor_Speed_Event`.

A method's Exec command and ExecResult event are named from the method's NodeId the same way: `ns=2;s=Pump.Start` gets `Exec_ns2_s_Pump_Start_Command` and `ExecResult_ns2_s_Pump_Start_Event`. A method node that several objects share is called on each of them separately, so each pair is named with the object first, then the method, separated by a double underscore: `Exec_ns2_s_Pump1__ns2_s_Start_Command` and `Exec_ns2_s_Pump2__ns2_s_Start_Command`.

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
| `BadWaitingForInitialData` | The bridge hasn't discovered the server yet. Nothing was saved; send the command again. `NodeId` is empty. |
| `BadNodeIdUnknown` | The variable is not on the server, or no longer is. A Delete for a variable that has left the server still deletes your monitor on it and is answered `Deleted`. |
| `BadNotFound` | Delete: you have no monitor on that variable. |
| `BadNotConnected` | `Pending`: saved, and applied when the server is back. |
| `BadDeadbandFilterInvalid` / `BadFilterNotAllowed` | The server refused the deadband, for example `Percent` on a variable that is not an AnalogItem. The monitor is kept and retried on every reconnect. |

## Set

A variable gets a Set command when the session's user may write it (its `UserAccessLevel` has `CurrentWrite`), it matches `OPCUA_WRITE_FILTER` if that is set, and its value can be written back from JSON:

- a scalar or one-dimensional array of Boolean, the integer types, Float, Double, String, DateTime, Guid, ByteString, StatusCode or an enumeration; or
- a structure the bridge can decode, or a one-dimensional array of one, when the structure and every structure inside it name a default binary encoding and every other field is one of the types above.

Matrices, variables whose rank isn't fixed, the abstract types Number, Integer and UInteger, and values published as a string (NodeId, LocalizedText, QualifiedName, structures the bridge can't decode, ...) get no Set command. Discovery logs how many writable variables were left out and why, e.g. `3 writable items have no Set command (matrix or unfixed rank: 1, abstract type: 1, string fallback: 1)`, and each one at verbose level.

The Set command has one property, `Value`, typed exactly like the Get event's `Value`:

```json
{ "Value": 1450.5 }
```

- **Strict conversion.** The value is written as the variable's exact built-in type (an Int16 variable gets an Int16). JSON of the wrong kind (`"5"` or `3.7` for an Int16) is refused with `BadTypeMismatch`, and a number the type can't hold (`70000` for an Int16) with `BadOutOfRange`; nothing is rounded or wrapped. A DateTime without an offset is taken as UTC.
- **Structures.** Every field is required, except String, ByteString and array fields, which are written as null when left out. That is how the Get event leaves out null fields, so a Get event's `Value` can be sent back unchanged. A property the structure doesn't have is refused with `BadTypeMismatch`.
- **Arrays** are written whole; there is no way to set a single element. A Byte array may be sent as base64, which is how Get publishes it, or as an array of numbers.
- **DateTime** must be ISO 8601 (`2026-03-04T05:06:07Z`, as Get publishes it); other forms such as `03/04/2026` are refused rather than guessed.
- **Order.** Sets are served in the order they arrive, together with every other command (see [Order](#order)). Sets to different variables that arrive together are batched into one OPC UA Write.
- **No retries.** A Set is written once or not at all. When the OPC UA server is unreachable it fails at once with `BadNotConnected`; nothing waits for the server to come back.

### Set status

Every Set command is answered with the variable's SetStatus event, sent to you as a response:

```json
{ "NodeId": "ns=2;s=Machine.Motor.Speed", "StatusCode": 0, "Status": "Good" }
```

| Status | Meaning |
|---|---|
| `Good` | The server accepted the value. |
| `BadTypeMismatch` | `Value` is missing, or of the wrong kind, or a structure field is missing or unknown. Nothing was written. |
| `BadOutOfRange` | A number doesn't fit the variable's type, or the server refused the value as out of range. |
| `BadNotWritable` | The variable is readable but has no Set command. |
| `BadNodeIdUnknown` | The variable is not on the server, or no longer is. `NodeId` is empty. |
| `BadWaitingForInitialData` | The bridge hasn't discovered the server yet. Send the command again. `NodeId` is empty. |
| `BadNotConnected` | The OPC UA server is unreachable. Nothing was written. |
| `BadTimeout`, `BadCommunicationError` | **The outcome is unknown**: the write may have reached the server before the connection failed. Send a Get to find out. |
| any other | The server's own reason, e.g. `BadUserAccessDenied`. |

A Set whose reply-to is on another broker or uses other credentials than the bridge is dropped unanswered, and nothing is written.

## Exec

A method gets an Exec command when it is a component of an object below the Objects folder, both its `Executable` and `UserExecutable` attributes are true, it matches `OPCUA_METHOD_FILTER` if that is set, and every input argument can be written from JSON, by the same rules as a Set's `Value`. The server's own methods (namespace 0 and the Diagnostics namespace, such as `Server.GetMonitoredItems`) are never exposed, even with `OPCUA_INCLUDE_NS0`. A method with an input that can't be written (a matrix, an abstract type, a type published as a string, ...) gets no Exec command; discovery logs how many and why, e.g. `1 executable methods have no Exec command (matrix or unfixed rank: 1)`. Outputs never hide a method: an output with no natural JSON form is published as a string, as Get does.

The Exec command has one property per input argument, named after the argument, typed exactly like a Set's `Value`. For a method `Add(Int32 a, Int32 b) → Int32 sum`:

```json
{ "a": 2, "b": 3 }
```

Every input is required. A method without inputs takes `{}` (or an empty body). An argument with no name becomes `Arg0`, `Arg1`, ... by position, a name that isn't an identifier has its other characters replaced by `_`, and repeated names get `_2`, `_3`, ....

The ExecResult event is sent to you as a response:

```json
{ "ObjectId": "ns=2;s=Methods", "MethodId": "ns=2;s=Methods.Add", "StatusCode": 0, "Status": "Good", "sum": 5 }
```

It has one property per output argument, typed like a Get event's `Value`, present only when the call did not fail (an output the server didn't return is left out). An output named `ObjectId`, `MethodId`, `StatusCode` or `Status` gets a `_2` suffix.

| Status | Meaning |
|---|---|
| `Good`, `Uncertain...` | The method ran; the outputs are included. |
| `BadArgumentsMissing` | The body isn't a JSON object, or an input is missing. The method wasn't called. |
| `BadTooManyArguments` | The body has a property that is no input. The method wasn't called. |
| `BadTypeMismatch`, `BadOutOfRange` | An input is of the wrong kind or doesn't fit its type, as for a Set. The method wasn't called. |
| `BadInvalidArgument` | The server rejected inputs; `Status` names each one and why, e.g. `BadInvalidArgument: percent (BadOutOfRange)`. |
| `BadMethodInvalid` | The method isn't exposed: not on the server, filtered out, or not executable. `ObjectId` and `MethodId` are empty. |
| `BadWaitingForInitialData` | The bridge hasn't discovered the server yet. Send the command again. `ObjectId` and `MethodId` are empty. |
| `BadNotConnected` | The OPC UA server is unreachable. The method wasn't called. |
| `BadCommunicationError`, `BadSessionClosed`, ... after the call started | **The outcome is unknown**: the method may have run before the connection failed. |
| any other | The server's own reason, e.g. `BadUserAccessDenied`. |

- **No timeout.** The bridge waits as long as the method takes. Calls go over a second OPC UA session of their own, opened on the first call, because the OPC UA client cuts every request off at the session's 15 s operation timeout. While a method runs, every command behind it waits (see [Order](#order)); a method that never returns holds them until the OPC UA connection drops. Monitor notifications keep flowing.
- **No retries.** A method is called once or not at all, and nothing waits for an unreachable server to come back.
- An Exec whose reply-to is on another broker or uses other credentials than the bridge is dropped unanswered, and the method isn't called.

## Order

The bridge serves every Get, Set, Exec, Monitor and Delete command one at a time **in the order it receives them**, and sends the answers in that order, refusals included. A Set followed by a Get of the same variable reads the value just written; an Exec sent after a Set runs after the value was written. Consecutive Gets are still batched into one OPC UA Read and consecutive Sets into one Write.

Answers carry no correlation id, so match each answer to your command by order. If an answer is lost -- the broker was unreachable when the bridge sent it -- matching by order is out of step from then on; re-synchronise with a Get before relying on it again.

`GetSelfDescriptionCommand` is answered at once, outside this order.

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
| `OPCUA_WRITE_FILTER` | | Regex matched against the NodeId text. When set, only matching writable nodes get a Set command; the others stay readable. |
| `OPCUA_METHOD_FILTER` | | Regex matched against the method's NodeId text. When set, only matching executable methods get an Exec command. |
| `OPCUA_RECONNECT_SECONDS` | `5` | Delay before reconnecting after the session is lost. |

### Monitors

| Variable | Default | Description |
|---|---|---|
| `MONITOR_STORE_PATH` | `/usr/share/StatePipes/StatePipes.OpcUaBridge/Config/monitors.json` | Where monitors are saved. Mount the directory as a volume to keep monitors when the container is re-created. An unreadable file, or one written by an earlier bridge version, is renamed to `monitors.json.corrupt-<time>` and the bridge starts with no monitors. |
| `MONITOR_SUSPENDED_EXPIRY_HOURS` | *(never)* | Delete monitors that stay suspended this long. |

On Kafka, suspension needs the broker setting `auto.create.topics.enable=false`. Otherwise the next data change recreates a departed client's response topic, and its monitors keep sending to it.

Discovery browses down from the Objects folder. It runs again after every reconnect, so a restarted or reconfigured server is picked up automatically. While the server is unreachable, Get commands are still answered, with a Bad status.

### Large servers

The self description lists all five messages of every variable in **one message**, at roughly 10.7 KB per variable (about 3.3 KB of it for the Get command and event). A writable variable's Set command and SetStatus event add about 2.6 KB more for a plain value, and more for a structure, whose description each message repeats. Brokers cap message size by default: 16 MiB for RabbitMQ 4 and 1 MiB for Kafka. That works out to about 1,500 variables on RabbitMQ and about 95 on Kafka. The bridge logs the size at startup and logs an error when it's over the limit. To stay under it, narrow the exposed nodes with `OPCUA_NODE_FILTER` (e.g. `^ns=3;`), the writable ones with `OPCUA_WRITE_FILTER` and the methods with `OPCUA_METHOD_FILTER`, or raise the broker's limit. A method's Exec command and ExecResult event add about 3.2 KB for two plain inputs and one output, and more for structures.

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

To try it without a PLC, use the `OpcUaServer` project in this repository (its `Setpoints` nodes are for trying out Set; see its README), or Microsoft's simulator: `docker run -d -p 50000:50000 mcr.microsoft.com/iotedge/opc-plc --pn=50000 --autoaccept`. Its telemetry nodes are in `ns=3`.

## Design notes

- The bridge doesn't host a `StatePipesService`. Its Get commands only exist at runtime, and a compiled service dispatches on compiled message types. Instead it speaks the StatePipes wire format directly over BrokerProxy's RabbitMQ/Kafka channels, which it compiles as linked source.
- Get commands are queued off the broker's receive thread. A single reader then batches whatever has queued up into one OPC UA Read.
- Set commands are queued and batched the same way into one OPC UA Write, except that a batch never holds two Sets for the same variable, because OPC UA doesn't promise to apply one Write's values in order.
- Every monitor change (commands, rebuilds, suspension, expiry) goes through one queue with a single reader, so a command never races a rebuild.
- Monitor responses use a second broker channel. RabbitMQ closes a channel that publishes to a missing exchange; on its own channel that never interrupts commands or heartbeats, and it triggers an immediate reply-to check.
