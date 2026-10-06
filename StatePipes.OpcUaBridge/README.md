# StatePipes.OpcUaBridge

A docker container that presents an OPC UA server as a StatePipes service on a RabbitMQ or Kafka broker.

On startup, OpcUaBridge connects to the OPC UA server and finds every variable the session can read. For each one it exposes a **Get command**. When it receives a Get command, it reads the variable and sends a **Get event** containing the value back to the sender as a response. The bridge also answers `GetSelfDescriptionCommand` and publishes `HeartbeatEvent`, so StatePipes.Explorer and generated proxies treat it like any other service.

## Naming

Each name comes from the node's namespace index and identifier, with `.` replaced by `_`:

| OPC UA node | Get command | Get event |
|---|---|---|
| `ns=2;s=Line1.Temperature` | `StatePipes.OpcUaBridge.Commands.Get_ns2_s_Line1_Temperature` | `StatePipes.OpcUaBridge.Events.Get_ns2_s_Line1_Temperature` |
| `ns=3;i=1001` | `StatePipes.OpcUaBridge.Commands.Get_ns3_i_1001` | `StatePipes.OpcUaBridge.Events.Get_ns3_i_1001` |

- Any other character that can't appear in a type name also becomes `_`.
- Numeric (`i`), GUID (`g`) and opaque (`b`) identifiers keep their own letter, so they never collide with string identifiers.
- If two nodes still end up with the same name (for example `A.B` and `A_B`), the later one in NodeId order gets a `_2` suffix, and a warning is logged.
- Names too long for a 255-byte RabbitMQ routing key are shortened and end with a hash of the NodeId.

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
| `OPCUA_INCLUDE_NS0` | `false` | Also expose namespace 0 (the server's own diagnostics). |
| `OPCUA_NODE_FILTER` | | Regex matched against the NodeId text. When set, only matching nodes are exposed. |
| `OPCUA_RECONNECT_SECONDS` | `5` | Delay before reconnecting after the session is lost. |

Discovery browses down from the Objects folder. It runs again after every reconnect, so a restarted or reconfigured server is picked up automatically. While the server is unreachable, Get commands are still answered, with a Bad status.

### Large servers

The self description lists every Get command and event in **one message**, at roughly 3 KB per variable. Brokers cap message size by default: 16 MiB for RabbitMQ 4 and 1 MiB for Kafka. That works out to about 5,000 variables on RabbitMQ and about 300 on Kafka. The bridge logs the size at startup and logs an error when it's over the limit. To stay under it, narrow the exposed nodes with `OPCUA_NODE_FILTER` (e.g. `^ns=3;`) or raise the broker's limit.

## Running

```sh
docker run -d --name opcuabridge \
  -v /path/to/certs:/usr/share/StatePipes/StatePipes.OpcUaBridge/Certs:ro \
  -v /path/to/root_ca.pem:/etc/ssl/certs/root_ca.pem:ro \
  -v /path/to/intermediate_ca.pem:/etc/ssl/certs/intermediate_ca.pem:ro \
  -v opcuabridge-pki:/pki \
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
