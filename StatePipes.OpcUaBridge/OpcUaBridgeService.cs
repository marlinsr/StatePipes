using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.BrokerProxy;
using StatePipes.Common;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Messages;
using System.Text;
using System.Threading.Channels;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// The StatePipes half of the bridge: presents the OPC UA server as a StatePipes service on the broker.
    ///
    /// <para>It speaks the StatePipes wire protocol directly over BrokerProxy's channels rather than hosting a
    /// <c>StatePipesService</c>, because the Get commands only exist at runtime and a compiled service dispatches
    /// on compiled message types. To clients it behaves like any other service: it publishes
    /// <see cref="HeartbeatEvent"/> so proxies see it as connected, answers <see cref="GetSelfDescriptionCommand"/>
    /// with the discovered commands and events, and answers each Get command with its Get event as a response
    /// to the sender.</para>
    ///
    /// <para>Commands arrive on the transport's receive thread, which must not block, so Get commands are queued
    /// and a single reader serves them, batching whatever has queued up into one OPC UA Read.</para>
    /// </summary>
    internal sealed class OpcUaBridgeService(BusConfig busConfig, OpcUaClient opcUa) : IDisposable
    {
        private const int MaxReadBatch = 500;
        // Brokers' default caps on a single message: RabbitMQ 4 max_message_size and Kafka message.max.bytes.
        private const int RabbitMqDefaultMaxMessageBytes = 16 * 1024 * 1024;
        private const int KafkaDefaultMaxMessageBytes = 1024 * 1024;
        private static readonly string SelfDescriptionCommandName = typeof(GetSelfDescriptionCommand).FullName!;
        private static readonly string SelfDescriptionEventName = typeof(SelfDescriptionEvent).FullName!;
        private static readonly string HeartbeatEventName = typeof(HeartbeatEvent).FullName!;

        private readonly Guid _id = Guid.NewGuid();
        private readonly byte[] _ownReplyTo = Encoding.UTF8.GetBytes(JsonUtility.GetJsonStringForObject(busConfig, true));
        private readonly Channel<PendingGet> _pendingGets = Channel.CreateUnbounded<PendingGet>(new UnboundedChannelOptions { SingleReader = true });
        private ISimpleConnectionChannel? _connectionChannel;
        private volatile Catalog _catalog = Catalog.Create([]);
        private long _heartbeatCounter;

        private sealed record PendingGet(OpcUaDataItem Item, BusConfig ReplyTo, byte[] ReplyToRaw);

        /// <summary>What the bridge currently exposes, swapped as a whole whenever the server is rediscovered.</summary>
        private sealed class Catalog(Dictionary<string, OpcUaDataItem> byCommand, byte[] selfDescriptionBody)
        {
            public Dictionary<string, OpcUaDataItem> ByCommand { get; } = byCommand;
            public byte[] SelfDescriptionBody { get; } = selfDescriptionBody;
            public static Catalog Create(IReadOnlyList<OpcUaDataItem> items)
            {
                var selfDescription = new SelfDescriptionEvent(SelfDescriptionBuilder.Build(items));
                return new(items.ToDictionary(item => item.CommandTypeFullName, StringComparer.Ordinal),
                    Encoding.UTF8.GetBytes(JsonUtility.GetJsonStringForObject(selfDescription, true)));
            }
        }

        public void SetCatalog(IReadOnlyList<OpcUaDataItem> items)
        {
            var catalog = Catalog.Create(items);
            _catalog = catalog;
            var size = catalog.SelfDescriptionBody.Length;
            Log?.LogInfo($"Exposing {items.Count} OPC UA data items as Get commands on {busConfig.CommandExchangeName} (self description {size / 1024} KiB)");
            var limit = busConfig.TransportKind == TransportKind.Kafka ? KafkaDefaultMaxMessageBytes : RabbitMqDefaultMaxMessageBytes;
            if (size > limit)
                Log?.LogError($"The self description is {size / 1024} KiB, over the {limit / 1024} KiB default {busConfig.TransportKind} message limit, so clients may never receive it. Narrow the exposed nodes with OPCUA_NODE_FILTER or raise the broker's limit.");
        }

        public Task RunAsync(CancellationToken ct) => Task.WhenAll(MaintainBrokerConnectionAsync(ct), PublishHeartbeatsAsync(ct), ServeGetsAsync(ct));

        private async Task MaintainBrokerConnectionAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_connectionChannel == null)
                {
                    try { _connectionChannel = SimpleConnectionChannelFactory.Create(busConfig, null, ConfigureBuses, ct); }
                    catch (Exception ex) { Log?.LogVerbose($"Could not connect to broker {busConfig.BrokerUri}: {ex.Message}"); }
                }
                try { await Task.Delay(TransportConstants.HeartbeatIntervalMilliseconds, ct); } catch (OperationCanceledException) { }
            }
        }

        private void ConfigureBuses(ISimpleConnectionChannel connectionChannel)
        {
            try
            {
                connectionChannel.ConfigureBus(_id, CommunicationsType.Command, busConfig.CommandExchangeName, ConsumeCommand, ISimpleConnectionChannel.DefaultRoutingKeys);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Event, busConfig.EventExchangeName);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }

        /// <summary>
        /// Proxies decide a service is alive from a rising HeartbeatEvent counter, the same one a compiled
        /// service publishes from its periodic HeartbeatCommand.
        /// </summary>
        private async Task PublishHeartbeatsAsync(CancellationToken ct)
        {
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(TransportConstants.HeartbeatIntervalMilliseconds));
            try
            {
                do
                {
                    var heartbeat = new HeartbeatEvent(unchecked(_heartbeatCounter++));
                    Send(Encoding.UTF8.GetBytes(JsonUtility.GetJsonStringForObject(heartbeat, true)), HeartbeatEventName, _ownReplyTo, busConfig.EventExchangeName);
                }
                while (await timer.WaitForNextTickAsync(ct));
            }
            catch (OperationCanceledException) { }
        }

        private void ConsumeCommand(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw)
        {
            try
            {
                var catalog = _catalog;
                if (routingKey == SelfDescriptionCommandName)
                {
                    if (TryGetReplyTo(replyToRaw, out var replyTo)) SendResponse(catalog.SelfDescriptionBody, SelfDescriptionEventName, replyTo, replyToRaw.ToArray());
                }
                else if (catalog.ByCommand.TryGetValue(routingKey, out var item))
                {
                    if (TryGetReplyTo(replyToRaw, out var replyTo)) _pendingGets.Writer.TryWrite(new PendingGet(item, replyTo, replyToRaw.ToArray()));
                }
                else
                {
                    Log?.LogVerbose($"Ignoring unsupported command {routingKey}");
                }
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }

        private async Task ServeGetsAsync(CancellationToken ct)
        {
            List<PendingGet> batch = [];
            try
            {
                while (await _pendingGets.Reader.WaitToReadAsync(ct))
                {
                    batch.Clear();
                    while (batch.Count < MaxReadBatch && _pendingGets.Reader.TryRead(out var pending)) batch.Add(pending);
                    var values = await ReadAsync(batch, ct);
                    for (var i = 0; i < batch.Count; i++)
                    {
                        var pending = batch[i];
                        SendResponse(CreateGetEventBody(pending.Item, values[i]), pending.Item.EventTypeFullName, pending.ReplyTo, pending.ReplyToRaw);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task<IReadOnlyList<DataValue>> ReadAsync(List<PendingGet> batch, CancellationToken ct)
        {
            try
            {
                return await opcUa.ReadValuesAsync([.. batch.Select(pending => pending.Item.NodeId)], ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log?.LogError($"OPC UA read of {batch.Count} items failed: {ex.Message}");
                var status = ex is ServiceResultException sre ? sre.StatusCode : StatusCodes.BadCommunicationError;
                return [.. batch.Select(_ => new DataValue(status))];
            }
        }

        /// <summary>
        /// A missing value is left out rather than sent as null. Clients that emit types from the self
        /// description (StatePipes.Explorer) currently give a nullable value-type property its plain type, which
        /// cannot hold a JSON null; an absent property deserializes everywhere, and StatusCode says why.
        /// </summary>
        internal static byte[] CreateGetEventBody(OpcUaDataItem item, DataValue dataValue)
        {
            JObject getEvent = new() { [SelfDescriptionBuilder.NodeIdProperty] = item.NodeIdText };
            var value = StatusCode.IsBad(dataValue.StatusCode) ? JValue.CreateNull() : OpcUaValueMapper.ToJToken(dataValue.Value, item.ValueType);
            if (value.Type != JTokenType.Null) getEvent[SelfDescriptionBuilder.ValueProperty] = value;
            getEvent[SelfDescriptionBuilder.StatusCodeProperty] = dataValue.StatusCode.Code;
            getEvent[SelfDescriptionBuilder.StatusProperty] = dataValue.StatusCode.ToString();
            getEvent[SelfDescriptionBuilder.SourceTimestampProperty] = dataValue.SourceTimestamp;
            getEvent[SelfDescriptionBuilder.ServerTimestampProperty] = dataValue.ServerTimestamp;
            return Encoding.UTF8.GetBytes(getEvent.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static bool TryGetReplyTo(ReadOnlyMemory<byte> replyToRaw, out BusConfig replyTo)
        {
            replyTo = JsonUtility.GetObjectForJsonString<BusConfig>(Encoding.UTF8.GetString(replyToRaw.Span))!;
            if (replyTo != null) return true;
            Log?.LogError($"Failed to deserialize BusConfig from {MessageHelper.StatePipesReplyToHeader} header; cannot respond");
            return false;
        }

        /// <summary>
        /// Sends a response to the client that issued the command, guarded exactly as StatePipesService guards
        /// it: responses can only travel on this broker and under this bridge's own credentials. The reply-to
        /// header goes back out as received, which is what the client's response consumer expects.
        /// </summary>
        private void SendResponse(byte[] body, string routingKey, BusConfig replyTo, byte[] replyToRaw)
        {
            if (replyTo.BrokerUri != busConfig.BrokerUri)
            {
                Log?.LogError($"Can't send response {routingKey} because it is on broker {replyTo.BrokerUri}");
                return;
            }
            if (replyTo.ClientCertPath != busConfig.ClientCertPath)
            {
                Log?.LogError($"Can't send response {routingKey} because it uses a {replyTo.ClientCertPath} certificate for authentication");
                return;
            }
            Send(body, routingKey, replyToRaw, replyTo.ResponseExchangeName);
        }

        private void Send(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw, string exchangeName)
        {
            try
            {
                var channel = _connectionChannel;
                if (channel is { IsOpen: true }) channel.Send(body, routingKey, replyToRaw, exchangeName);
                else Log?.LogVerbose($"Failed to send {routingKey}: not connected to broker");
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }

        public void Dispose()
        {
            _pendingGets.Writer.TryComplete();
            _connectionChannel?.Dispose();
            _connectionChannel = null;
        }
    }
}
