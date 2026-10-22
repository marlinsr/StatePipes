using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.BrokerProxy;
using StatePipes.Common;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Messages;
using StatePipes.OpcUaBridge.Messages;
using System.Text;
using System.Threading.Channels;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>Creates a broker channel; the default is BrokerProxy's factory, tests substitute their own.</summary>
    internal delegate ISimpleConnectionChannel ConnectionChannelFactory(BusConfig busConfig, Action<ISimpleConnectionChannel>? configureBuses, CancellationToken ct);

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
    /// <para>Commands arrive on the transport's receive thread, which must not block, so every command but
    /// GetSelfDescription -- Get, Set, Exec, Monitor, Delete, and the refusals of commands for nodes and methods
    /// the bridge does not expose -- is queued on a <see cref="CommandExecutor"/>, which serves them one at a time
    /// in arrival order. Monitor and Delete, when their turn comes, are handed to <see cref="Monitors"/> and waited
    /// for.</para>
    ///
    /// <para>Monitor responses -- statuses and data changes -- travel on a second channel. A client's response
    /// exchange is auto-delete, and RabbitMQ closes a channel that publishes to a missing exchange; on its own
    /// channel that only interrupts other monitor traffic, never commands or heartbeats, and it prompts a check
    /// that suspends the monitors whose reply-to has gone.</para>
    /// </summary>
    internal sealed class OpcUaBridgeService : IMonitorOutput, ICommandOutput, IDisposable
    {
        // Brokers' default caps on a single message: RabbitMQ 4 max_message_size and Kafka message.max.bytes.
        private const int RabbitMqDefaultMaxMessageBytes = 16 * 1024 * 1024;
        private const int KafkaDefaultMaxMessageBytes = 1024 * 1024;
        private static readonly string SelfDescriptionCommandName = typeof(GetSelfDescriptionCommand).FullName!;
        private static readonly string SelfDescriptionEventName = typeof(SelfDescriptionEvent).FullName!;
        private static readonly string HeartbeatEventName = typeof(HeartbeatEvent).FullName!;

        private readonly BusConfig _busConfig;
        private readonly OpcUaClient _opcUa;
        private readonly ConnectionChannelFactory _channelFactory;
        private readonly string _namespace;
        private readonly Guid _id = Guid.NewGuid();
        private readonly byte[] _ownReplyTo;
        private readonly CommandExecutor _executor;
        private ISimpleConnectionChannel? _connectionChannel;
        private ISimpleConnectionChannel? _notificationChannel;
        private volatile Catalog _catalog = Catalog.Create([], [], discovered: false);
        private long _heartbeatCounter;
        private int _replyToCheckRequested;

        /// <param name="session">Where commands are served; the OPC UA client unless a test substitutes its own.</param>
        public OpcUaBridgeService(BusConfig busConfig, OpcUaClient opcUa, MonitorRegistry registry, TimeProvider? time = null, ConnectionChannelFactory? channelFactory = null, IOpcUaSession? session = null)
        {
            _executor = new CommandExecutor(session ?? opcUa, this);
            _busConfig = busConfig;
            _opcUa = opcUa;
            _channelFactory = channelFactory ?? ((config, configure, ct) => SimpleConnectionChannelFactory.Create(config, null, configure, ct));
            _namespace = OpcUaNaming.MessageNamespace(busConfig.ExchangeNamePrefix);
            _ownReplyTo = Encoding.UTF8.GetBytes(JsonUtility.GetJsonStringForObject(busConfig, true));
            Monitors = new MonitorCoordinator(registry, this, time ?? TimeProvider.System, busConfig.ExchangeNamePrefix);
        }

        public MonitorCoordinator Monitors { get; }

        /// <summary>What the bridge currently exposes, swapped as a whole whenever the server is rediscovered.</summary>
        private sealed class Catalog(Dictionary<string, OpcUaDataItem> byCommand, Dictionary<string, OpcUaDataItem> byMonitorCommand, Dictionary<string, OpcUaDataItem> byDeleteCommand,
            Dictionary<string, OpcUaDataItem> bySetCommand, Dictionary<string, OpcUaMethodItem> byExecCommand, byte[] selfDescriptionBody, bool discovered)
        {
            public Dictionary<string, OpcUaDataItem> ByCommand { get; } = byCommand;
            public Dictionary<string, OpcUaDataItem> ByMonitorCommand { get; } = byMonitorCommand;
            public Dictionary<string, OpcUaDataItem> ByDeleteCommand { get; } = byDeleteCommand;
            /// <summary>The writable items only.</summary>
            public Dictionary<string, OpcUaDataItem> BySetCommand { get; } = bySetCommand;
            public Dictionary<string, OpcUaMethodItem> ByExecCommand { get; } = byExecCommand;
            public byte[] SelfDescriptionBody { get; } = selfDescriptionBody;
            /// <summary>False until the OPC UA server has been discovered once.</summary>
            public bool Discovered { get; } = discovered;
            public static Catalog Create(IReadOnlyList<OpcUaDataItem> items, IReadOnlyList<OpcUaMethodItem> methods, bool discovered)
            {
                var selfDescription = new SelfDescriptionEvent(SelfDescriptionBuilder.Build(items, methods));
                return new(items.ToDictionary(item => item.CommandTypeFullName, StringComparer.Ordinal),
                    items.ToDictionary(item => item.MonitorCommandTypeFullName, StringComparer.Ordinal),
                    items.ToDictionary(item => item.DeleteCommandTypeFullName, StringComparer.Ordinal),
                    items.Where(item => item.IsWritable).ToDictionary(item => item.SetCommandTypeFullName, StringComparer.Ordinal),
                    methods.ToDictionary(method => method.ExecCommandTypeFullName, StringComparer.Ordinal),
                    Encoding.UTF8.GetBytes(JsonUtility.GetJsonStringForObject(selfDescription, true)), discovered);
            }
        }

        public void SetCatalog(IReadOnlyList<OpcUaDataItem> items, IReadOnlyList<OpcUaMethodItem>? methods = null)
        {
            var catalog = Catalog.Create(items, methods ?? [], discovered: true);
            _catalog = catalog;
            var size = catalog.SelfDescriptionBody.Length;
            Log?.LogInfo($"Exposing {items.Count} OPC UA data items as Get, Monitor and Delete commands, {catalog.BySetCommand.Count} of them also as Set commands, and {catalog.ByExecCommand.Count} methods as Exec commands, on {_busConfig.CommandExchangeName} (self description {size / 1024} KiB)");
            var limit = _busConfig.TransportKind == TransportKind.Kafka ? KafkaDefaultMaxMessageBytes : RabbitMqDefaultMaxMessageBytes;
            if (size > limit)
                Log?.LogError($"The self description is {size / 1024} KiB, over the {limit / 1024} KiB default {_busConfig.TransportKind} message limit, so clients may never receive it. Narrow the exposed nodes with OPCUA_NODE_FILTER or raise the broker's limit.");
        }

        public Task RunAsync(CancellationToken ct) => Task.WhenAll(MaintainBrokerConnectionAsync(ct), PublishHeartbeatsAsync(ct), Monitors.RunAsync(ct), _executor.RunAsync(ct));

        private async Task MaintainBrokerConnectionAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_connectionChannel == null)
                {
                    try { _connectionChannel = _channelFactory(_busConfig, ConfigureBuses, ct); }
                    catch (Exception ex) { Log?.LogVerbose($"Could not connect to broker {_busConfig.BrokerUri}: {ex.Message}"); }
                }
                if (_notificationChannel == null)
                {
                    try { _notificationChannel = _channelFactory(_busConfig, null, ct); }
                    catch (Exception ex) { Log?.LogVerbose($"Could not open notification channel to broker {_busConfig.BrokerUri}: {ex.Message}"); }
                }
                try { await Task.Delay(TransportConstants.HeartbeatIntervalMilliseconds, ct); } catch (OperationCanceledException) { }
            }
        }

        private void ConfigureBuses(ISimpleConnectionChannel connectionChannel)
        {
            try
            {
                connectionChannel.ConfigureBus(_id, CommunicationsType.Command, _busConfig.CommandExchangeName, ConsumeCommand, ISimpleConnectionChannel.DefaultRoutingKeys);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Event, _busConfig.EventExchangeName);
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
                    Send(_connectionChannel, Encoding.UTF8.GetBytes(JsonUtility.GetJsonStringForObject(heartbeat, true)), HeartbeatEventName, _ownReplyTo, _busConfig.EventExchangeName);
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
                    if (TryGetReplyTo(replyToRaw, out var replyTo)) SendResponse(_connectionChannel, catalog.SelfDescriptionBody, SelfDescriptionEventName, replyTo, replyToRaw.ToArray());
                }
                // Everything below is served later, in arrival order, after the transport may have reused its buffers.
                else if (catalog.ByCommand.TryGetValue(routingKey, out var item))
                {
                    if (TryGetReplyTo(replyToRaw, out var replyTo)) _executor.EnqueueGet(item, replyTo, replyToRaw.ToArray());
                }
                else if (catalog.ByMonitorCommand.TryGetValue(routingKey, out var monitored))
                {
                    if (TryGetAllowedReplyTo(routingKey, replyToRaw, out var replyTo)) ConsumeMonitorCommand(body, monitored, replyTo, replyToRaw);
                }
                else if (catalog.ByDeleteCommand.TryGetValue(routingKey, out var deleted))
                {
                    if (!TryGetAllowedReplyTo(routingKey, replyToRaw, out var replyTo)) return;
                    var replyToText = Encoding.UTF8.GetString(replyToRaw.Span);
                    _executor.EnqueueAlone($"Delete of {deleted.NodeIdText}", ct => Monitors.DeleteAsync(replyTo.CommandExchangeName, deleted, replyToText, ct));
                }
                else if (catalog.BySetCommand.TryGetValue(routingKey, out var settable))
                {
                    if (TryGetAllowedReplyTo(routingKey, replyToRaw, out var replyTo)) _executor.EnqueueSet(settable, [.. body], replyTo, replyToRaw.ToArray());
                }
                else if (catalog.ByExecCommand.TryGetValue(routingKey, out var method))
                {
                    if (TryGetAllowedReplyTo(routingKey, replyToRaw, out var replyTo)) _executor.EnqueueExec(method, [.. body], replyTo, replyToRaw.ToArray());
                }
                else if (MessageNames.FromExecCommandFullName(routingKey, _namespace) is { } execNames)
                {
                    // An Exec for a method without one: not discovered yet, gone, filtered, or not executable.
                    if (!TryGetAllowedReplyTo(routingKey, replyToRaw, out var replyTo)) return;
                    var status = catalog.Discovered ? StatusCodes.BadMethodInvalid : StatusCodes.BadWaitingForInitialData;
                    var raw = replyToRaw.ToArray();
                    EnqueueRefusal(routingKey, () => SendExecResult(execNames, ExecResultBody.Refused(null, status), replyTo, raw));
                }
                else if (MessageNames.FromSetCommandFullName(routingKey, _namespace) is { } setNames)
                {
                    // A Set for a node without one: not discovered yet, gone, or not writable.
                    if (!TryGetAllowedReplyTo(routingKey, replyToRaw, out var replyTo)) return;
                    var readable = catalog.ByCommand.GetValueOrDefault(setNames.CommandTypeFullName);
                    var status = !catalog.Discovered ? StatusCodes.BadWaitingForInitialData : readable != null ? StatusCodes.BadNotWritable : StatusCodes.BadNodeIdUnknown;
                    var raw = replyToRaw.ToArray();
                    EnqueueRefusal(routingKey, () => SendSetStatus(setNames, readable?.NodeIdText ?? string.Empty, status, replyTo, raw));
                }
                else if (MessageNames.FromCommandFullName(routingKey, _namespace) is { } names)
                {
                    // A Monitor or Delete command for a node the catalog does not hold: not discovered yet, or gone.
                    // A Delete still removes a stored monitor on that node, which may well have left the server.
                    if (!TryGetAllowedReplyTo(routingKey, replyToRaw, out var replyTo)) return;
                    var status = catalog.Discovered ? StatusCodes.BadNodeIdUnknown : StatusCodes.BadWaitingForInitialData;
                    var replyToText = Encoding.UTF8.GetString(replyToRaw.Span);
                    if (routingKey == names.DeleteCommandTypeFullName)
                        _executor.EnqueueAlone($"Delete {routingKey}", ct => Monitors.DeleteUnlistedAsync(replyTo.CommandExchangeName, names, replyToText, status, ct));
                    else EnqueueRefusal(routingKey, () => SendStatus(MonitorStatus.Failed(string.Empty, status), names, replyToText));
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

        private void ConsumeMonitorCommand(byte[] body, OpcUaDataItem item, BusConfig replyTo, ReadOnlyMemory<byte> replyToRaw)
        {
            var parsed = MonitorParameters.Parse(body);
            var replyToText = Encoding.UTF8.GetString(replyToRaw.Span);
            if (parsed.Parameters is not { } parameters) EnqueueRefusal(item.MonitorCommandTypeFullName, () => SendStatus(MonitorStatus.Failed(item.NodeIdText, parsed.Status), item.Names, replyToText));
            else _executor.EnqueueAlone($"Monitor of {item.NodeIdText}", ct => Monitors.MonitorAsync(replyTo.CommandExchangeName, item, parameters, replyToText, ct));
        }

        /// <summary>Answers a command the bridge will not serve on its turn, so answers keep arrival order.</summary>
        private void EnqueueRefusal(string routingKey, Action answer) => _executor.EnqueueAlone($"Refusing {routingKey}", _ =>
        {
            answer();
            return Task.CompletedTask;
        });

        /// <summary>
        /// A missing value is left out rather than sent as null. Clients that emit types from the self
        /// description (StatePipes.Explorer) currently give a nullable value-type property its plain type, which
        /// cannot hold a JSON null; an absent property deserializes everywhere, and StatusCode says why.
        /// </summary>
        internal static byte[] CreateGetEventBody(OpcUaDataItem item, DataValue dataValue)
        {
            JObject getEvent = new() { [SelfDescriptionBuilder.NodeIdProperty] = item.NodeIdText };
            var value = StatusCode.IsBad(dataValue.StatusCode) ? JValue.CreateNull() : ToJToken(item, dataValue.WrappedValue);
            if (value.Type != JTokenType.Null) getEvent[SelfDescriptionBuilder.ValueProperty] = value;
            getEvent[SelfDescriptionBuilder.StatusCodeProperty] = dataValue.StatusCode.Code;
            getEvent[SelfDescriptionBuilder.StatusProperty] = dataValue.StatusCode.ToString();
            getEvent[SelfDescriptionBuilder.SourceTimestampProperty] = dataValue.SourceTimestamp.ToDateTime();
            getEvent[SelfDescriptionBuilder.ServerTimestampProperty] = dataValue.ServerTimestamp.ToDateTime();
            return Encoding.UTF8.GetBytes(getEvent.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static JToken ToJToken(OpcUaDataItem item, object? value) => OpcUaValueMapper.ToJToken(value, item.ValueType, item.Structure, item.IsArray);

        public void SendStatus(MonitorStatus status, MessageNames names, string replyToRaw) => SendStatus(status, names.MonitorStatusEventTypeFullName, replyToRaw);

        /// <summary>Sends <paramref name="status"/> as the data item's MonitorStatus event, named <paramref name="statusEventFullName"/>.</summary>
        private void SendStatus(MonitorStatus status, string statusEventFullName, string replyToRaw)
        {
            var raw = Encoding.UTF8.GetBytes(replyToRaw);
            if (TryGetReplyTo(raw, out var replyTo)) SendMonitorResponse(CreateMonitorStatusBody(status), statusEventFullName, replyTo, raw);
        }

        internal static byte[] CreateMonitorStatusBody(MonitorStatus status)
        {
            JObject body = new()
            {
                [SelfDescriptionBuilder.NodeIdProperty] = status.NodeId,
                [SelfDescriptionBuilder.StateProperty] = status.State.ToString(),
                [SelfDescriptionBuilder.StatusCodeProperty] = status.StatusCode.Code,
                [SelfDescriptionBuilder.StatusProperty] = status.StatusCode.ToString(),
                [SelfDescriptionBuilder.RevisedSamplingIntervalProperty] = status.RevisedSamplingInterval,
                [SelfDescriptionBuilder.RevisedQueueSizeProperty] = status.RevisedQueueSize,
                [SelfDescriptionBuilder.RevisedPublishingIntervalProperty] = status.RevisedPublishingInterval
            };
            return Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None));
        }

        public void SendGetEvent(OpcUaDataItem item, DataValue value, BusConfig replyTo, byte[] replyToRaw) =>
            SendResponse(_connectionChannel, CreateGetEventBody(item, value), item.EventTypeFullName, replyTo, replyToRaw);

        /// <summary>Answers an Exec like a Set: once, on the main channel, to the sender's response exchange.</summary>
        public void SendExecResult(MessageNames names, byte[] body, BusConfig replyTo, byte[] replyToRaw) =>
            SendResponse(_connectionChannel, body, names.ExecResultEventTypeFullName, replyTo, replyToRaw);

        public void SendSetStatus(OpcUaDataItem item, StatusCode statusCode, BusConfig replyTo, byte[] replyToRaw) =>
            SendSetStatus(item.Names, item.NodeIdText, statusCode, replyTo, replyToRaw);

        /// <summary>Answers a Set like a Get: once, on the main channel, to the sender's response exchange.</summary>
        private void SendSetStatus(MessageNames names, string nodeId, StatusCode statusCode, BusConfig replyTo, byte[] replyToRaw) =>
            SendResponse(_connectionChannel, CreateSetStatusBody(nodeId, statusCode), names.SetStatusEventTypeFullName, replyTo, replyToRaw);

        internal static byte[] CreateSetStatusBody(string nodeId, StatusCode statusCode)
        {
            JObject body = new()
            {
                [SelfDescriptionBuilder.NodeIdProperty] = nodeId,
                [SelfDescriptionBuilder.StatusCodeProperty] = statusCode.Code,
                [SelfDescriptionBuilder.StatusProperty] = statusCode.ToString()
            };
            return Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None));
        }

        public void SendNotification(OpcUaDataItem item, DataValue value, string replyToRaw)
        {
            var raw = Encoding.UTF8.GetBytes(replyToRaw);
            if (TryGetReplyTo(raw, out var replyTo)) SendMonitorResponse(CreateGetEventBody(item, value), item.EventTypeFullName, replyTo, raw);
        }

        public Task<bool> ReplyToExistsAsync(BusConfig replyTo, CancellationToken ct)
        {
            var channel = _notificationChannel ?? _connectionChannel;
            return channel == null ? Task.FromResult(true) : channel.BusExistsAsync(replyTo.ResponseExchangeName, ct);
        }

        /// <summary>
        /// Sends on the notification channel. Finding it closed, typically because RabbitMQ shut it after a
        /// publish to a vanished response exchange, asks once for a reply-to check until it is open again.
        /// </summary>
        private void SendMonitorResponse(byte[] body, string routingKey, BusConfig replyTo, byte[] replyToRaw)
        {
            if (_notificationChannel is { IsOpen: true }) Interlocked.Exchange(ref _replyToCheckRequested, 0);
            else if (Interlocked.Exchange(ref _replyToCheckRequested, 1) == 0) Monitors.CheckReplyTos();
            SendResponse(_notificationChannel, body, routingKey, replyTo, replyToRaw);
        }

        private static bool TryGetReplyTo(ReadOnlyMemory<byte> replyToRaw, out BusConfig replyTo)
        {
            replyTo = JsonUtility.GetObjectForJsonString<BusConfig>(Encoding.UTF8.GetString(replyToRaw.Span))!;
            if (replyTo != null) return true;
            Log?.LogError($"Failed to deserialize BusConfig from {MessageHelper.StatePipesReplyToHeader} header; cannot respond");
            return false;
        }

        /// <summary>A reply-to the bridge may answer; monitor commands from anywhere else are dropped unanswered.</summary>
        private bool TryGetAllowedReplyTo(string routingKey, ReadOnlyMemory<byte> replyToRaw, out BusConfig replyTo)
        {
            if (!TryGetReplyTo(replyToRaw, out replyTo)) return false;
            if (ReplyToGuard.Allows(_busConfig, replyTo, out var reason)) return true;
            Log?.LogError($"Ignoring {routingKey} because its reply-to cannot be answered: {reason}");
            return false;
        }

        /// <summary>
        /// Sends a response to the client that issued the command, guarded exactly as StatePipesService guards
        /// it: responses can only travel on this broker and under this bridge's own credentials. The reply-to
        /// header goes back out as received, which is what the client's response consumer expects.
        /// </summary>
        private void SendResponse(ISimpleConnectionChannel? channel, byte[] body, string routingKey, BusConfig replyTo, byte[] replyToRaw)
        {
            if (!ReplyToGuard.Allows(_busConfig, replyTo, out var reason))
            {
                Log?.LogError($"Can't send response {routingKey} because {reason}");
                return;
            }
            Send(channel, body, routingKey, replyToRaw, replyTo.ResponseExchangeName);
        }

        private static void Send(ISimpleConnectionChannel? channel, byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw, string exchangeName)
        {
            try
            {
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
            _executor.Complete();
            _connectionChannel?.Dispose();
            _connectionChannel = null;
            _notificationChannel?.Dispose();
            _notificationChannel = null;
        }
    }
}
