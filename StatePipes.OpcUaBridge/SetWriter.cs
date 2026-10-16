using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.Comms;
using System.Text;
using System.Threading.Channels;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>Where SetStatus answers go: the broker side of the bridge, which applies the reply-to guards and never blocks.</summary>
    internal interface ISetOutput
    {
        /// <summary>Sends <paramref name="statusCode"/> as <paramref name="item"/>'s SetStatus event to the Set's sender.</summary>
        void SendSetStatus(OpcUaDataItem item, StatusCode statusCode, BusConfig replyTo, byte[] replyToRaw);
    }

    /// <summary>
    /// Writes Set commands to the OPC UA server and answers each with its SetStatus.
    ///
    /// <para>Sets are queued from the transport's receive thread and a single reader writes them, batching what
    /// has queued up into one OPC UA Write as Gets are batched into one Read. A batch never holds two Sets for the
    /// same node: OPC UA does not promise to apply a Write's values in order, so the second Set waits for the next
    /// batch, and Sets to one node are applied, and answered, in the order they arrived.</para>
    ///
    /// <para>A Set is written once or not at all: a failed write is reported, never retried, and nothing waits for
    /// a lost session to return, because a setpoint applied late is worse than one refused.</para>
    /// </summary>
    internal sealed class SetWriter(ISetSession session, ISetOutput output)
    {
        private const int MaxWriteBatch = 500;
        private static readonly JsonSerializerSettings BodySettings = new() { DateParseHandling = DateParseHandling.None };
        private readonly Channel<PendingSet> _pending = Channel.CreateUnbounded<PendingSet>(new UnboundedChannelOptions { SingleReader = true });

        private sealed record PendingSet(OpcUaDataItem Item, byte[] Body, BusConfig ReplyTo, byte[] ReplyToRaw);

        public void Enqueue(OpcUaDataItem item, byte[] body, BusConfig replyTo, byte[] replyToRaw) =>
            _pending.Writer.TryWrite(new PendingSet(item, body, replyTo, replyToRaw));

        public void Complete() => _pending.Writer.TryComplete();

        public async Task RunAsync(CancellationToken ct)
        {
            var reader = _pending.Reader;
            PendingSet? held = null;
            try
            {
                while (held != null || await reader.WaitToReadAsync(ct))
                {
                    List<PendingSet> batch = [];
                    HashSet<NodeId> nodes = [];
                    if (held != null)
                    {
                        batch.Add(held);
                        nodes.Add(held.Item.NodeId);
                        held = null;
                    }
                    while (batch.Count < MaxWriteBatch && reader.TryRead(out var next))
                    {
                        if (!nodes.Add(next.Item.NodeId))
                        {
                            held = next;
                            break;
                        }
                        batch.Add(next);
                    }
                    await WriteBatchAsync(batch, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task WriteBatchAsync(List<PendingSet> batch, CancellationToken ct)
        {
            var results = new StatusCode[batch.Count];
            List<int> toWrite = [];
            List<(NodeId, Variant)> values = [];
            for (var i = 0; i < batch.Count; i++)
            {
                results[i] = Encode(batch[i], out var value);
                if (StatusCode.IsBad(results[i])) continue;
                toWrite.Add(i);
                values.Add((batch[i].Item.NodeId, value));
            }
            if (values.Count > 0)
            {
                var written = await WriteAsync(values, ct);
                for (var i = 0; i < toWrite.Count; i++) results[toWrite[i]] = written[i];
            }
            for (var i = 0; i < batch.Count; i++) Answer(batch[i], results[i]);
        }

        private async Task<IReadOnlyList<StatusCode>> WriteAsync(List<(NodeId, Variant)> values, CancellationToken ct)
        {
            try
            {
                return await session.WriteValuesAsync(values, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Log?.LogError($"OPC UA write of {values.Count} items failed: {ex.Message}");
                StatusCode status = ex is ServiceResultException sre ? sre.StatusCode : StatusCodes.BadCommunicationError;
                return [.. values.Select(_ => status)];
            }
        }

        private static StatusCode Encode(PendingSet set, out Variant value)
        {
            value = Variant.Null;
            JObject? body;
            try { body = JsonConvert.DeserializeObject<JToken>(Encoding.UTF8.GetString(set.Body), BodySettings) as JObject; }
            catch (JsonException) { return StatusCodes.BadTypeMismatch; }
            var token = body?[SelfDescriptionBuilder.ValueProperty];
            var item = set.Item;
            return item.Structure != null
                ? StructureEncoder.TryEncode(token, item.Structure, item.IsArray, out value)
                : OpcUaValueEncoder.TryEncode(token, item.BuiltInType, item.IsArray, out value);
        }

        private void Answer(PendingSet set, StatusCode status)
        {
            if (StatusCode.IsBad(status)) Log?.LogWarning($"Set of {set.Item.NodeIdText} from {set.ReplyTo.CommandExchangeName} failed: {status}");
            else Log?.LogVerbose($"Set {set.Item.NodeIdText} from {set.ReplyTo.CommandExchangeName}: {status}");
            output.SendSetStatus(set.Item, status, set.ReplyTo, set.ReplyToRaw);
        }
    }
}
