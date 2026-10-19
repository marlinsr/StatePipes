using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.Comms;
using System.Text;
using System.Threading.Channels;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Serves every Get, Set, Exec, Monitor and Delete command, and every refusal, one at a time in the order the
    /// bridge received them, so a client sees them take effect and be answered in the order it sent them.
    ///
    /// <para>Commands are queued from the transport's receive thread and a single reader serves them. To keep the
    /// batching Gets and Sets had, the reader takes the longest run of one kind from the head of the queue: Gets
    /// become one OPC UA Read and Sets one Write, as long as no node repeats in the Write, because OPC UA does not
    /// promise to apply a Write's values in order. An Exec, and anything handed over with
    /// <see cref="EnqueueAlone"/>, is served on its own. A run is answered in full before the next one starts.</para>
    ///
    /// <para>Nothing is retried and nothing waits for a lost session to return: a setpoint applied, or a method
    /// called, late is worse than one refused. A call waits as long as the server takes, so a method that never
    /// returns holds every command behind it until the session drops.</para>
    /// </summary>
    internal sealed class CommandExecutor(IOpcUaSession session, ICommandOutput output)
    {
        private const int MaxBatch = 500;
        private static readonly JsonSerializerSettings BodySettings = new() { DateParseHandling = DateParseHandling.None };
        private readonly Channel<PendingCommand> _pending = Channel.CreateUnbounded<PendingCommand>(new UnboundedChannelOptions { SingleReader = true });

        private abstract record PendingCommand;
        private sealed record PendingGet(OpcUaDataItem Item, BusConfig ReplyTo, byte[] ReplyToRaw) : PendingCommand;
        private sealed record PendingSet(OpcUaDataItem Item, byte[] Body, BusConfig ReplyTo, byte[] ReplyToRaw) : PendingCommand;
        private sealed record PendingExec(OpcUaMethodItem Method, byte[] Body, BusConfig ReplyTo, byte[] ReplyToRaw) : PendingCommand;
        private sealed record PendingAlone(string Description, Func<CancellationToken, Task> Run) : PendingCommand;

        public void EnqueueGet(OpcUaDataItem item, BusConfig replyTo, byte[] replyToRaw) => _pending.Writer.TryWrite(new PendingGet(item, replyTo, replyToRaw));

        /// <param name="body">A copy the executor may keep: the transport may reuse its buffers.</param>
        public void EnqueueSet(OpcUaDataItem item, byte[] body, BusConfig replyTo, byte[] replyToRaw) => _pending.Writer.TryWrite(new PendingSet(item, body, replyTo, replyToRaw));

        /// <param name="body">A copy the executor may keep: the transport may reuse its buffers.</param>
        public void EnqueueExec(OpcUaMethodItem method, byte[] body, BusConfig replyTo, byte[] replyToRaw) => _pending.Writer.TryWrite(new PendingExec(method, body, replyTo, replyToRaw));

        /// <summary>
        /// Queues work served on its turn and alone -- a Monitor or Delete handed to the monitor coordinator, or a
        /// refusal -- and waited for before the next command. A failure is logged and the queue moves on.
        /// </summary>
        public void EnqueueAlone(string description, Func<CancellationToken, Task> run) => _pending.Writer.TryWrite(new PendingAlone(description, run));

        public void Complete() => _pending.Writer.TryComplete();

        public async Task RunAsync(CancellationToken ct)
        {
            var reader = _pending.Reader;
            PendingCommand? held = null;
            try
            {
                while (held != null || await reader.WaitToReadAsync(ct))
                {
                    var head = held;
                    held = null;
                    if (head == null && !reader.TryRead(out head)) continue;
                    switch (head)
                    {
                        case PendingGet get:
                            List<PendingGet> gets = [get];
                            while (gets.Count < MaxBatch && reader.TryRead(out var next))
                            {
                                if (next is PendingGet nextGet) gets.Add(nextGet);
                                else
                                {
                                    held = next;
                                    break;
                                }
                            }
                            await ServeGetsAsync(gets, ct);
                            break;
                        case PendingSet set:
                            List<PendingSet> sets = [set];
                            HashSet<NodeId> nodes = [set.Item.NodeId];
                            while (sets.Count < MaxBatch && reader.TryRead(out var next))
                            {
                                if (next is PendingSet nextSet && nodes.Add(nextSet.Item.NodeId)) sets.Add(nextSet);
                                else
                                {
                                    held = next;
                                    break;
                                }
                            }
                            await ServeSetsAsync(sets, ct);
                            break;
                        case PendingExec exec:
                            await ServeExecAsync(exec, ct);
                            break;
                        case PendingAlone alone:
                            await RunAloneAsync(alone, ct);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ServeGetsAsync(List<PendingGet> gets, CancellationToken ct)
        {
            IReadOnlyList<DataValue> values;
            try
            {
                values = await session.ReadValuesAsync([.. gets.Select(get => get.Item.NodeId)], ct);
            }
            catch (Exception ex) when (IsFailure(ex, ct))
            {
                Log?.LogError($"OPC UA read of {gets.Count} items failed: {ex.Message}");
                var status = ex is ServiceResultException sre ? sre.StatusCode : StatusCodes.BadCommunicationError;
                values = [.. gets.Select(_ => DataValue.FromStatusCode(status))];
            }
            for (var i = 0; i < gets.Count; i++) output.SendGetEvent(gets[i].Item, values[i], gets[i].ReplyTo, gets[i].ReplyToRaw);
        }

        private async Task ServeSetsAsync(List<PendingSet> sets, CancellationToken ct)
        {
            var results = new StatusCode[sets.Count];
            List<int> toWrite = [];
            List<(NodeId, Variant)> values = [];
            for (var i = 0; i < sets.Count; i++)
            {
                results[i] = Encode(sets[i], out var value);
                if (StatusCode.IsBad(results[i])) continue;
                toWrite.Add(i);
                values.Add((sets[i].Item.NodeId, value));
            }
            if (values.Count > 0)
            {
                var written = await WriteAsync(values, ct);
                for (var i = 0; i < toWrite.Count; i++) results[toWrite[i]] = written[i];
            }
            for (var i = 0; i < sets.Count; i++) AnswerSet(sets[i], results[i]);
        }

        private async Task<IReadOnlyList<StatusCode>> WriteAsync(List<(NodeId, Variant)> values, CancellationToken ct)
        {
            try
            {
                return await session.WriteValuesAsync(values, ct);
            }
            catch (Exception ex) when (IsFailure(ex, ct))
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

        private void AnswerSet(PendingSet set, StatusCode status)
        {
            if (StatusCode.IsBad(status)) Log?.LogWarning($"Set of {set.Item.NodeIdText} from {set.ReplyTo.CommandExchangeName} failed: {status}");
            else Log?.LogVerbose($"Set {set.Item.NodeIdText} from {set.ReplyTo.CommandExchangeName}: {status}");
            output.SendSetStatus(set.Item, status, set.ReplyTo, set.ReplyToRaw);
        }

        /// <summary>
        /// Encodes the inputs and calls the method. A Call that throws -- the session lost while the method ran,
        /// say -- leaves the outcome unknown: the method may have run.
        /// </summary>
        private async Task ServeExecAsync(PendingExec exec, CancellationToken ct)
        {
            var method = exec.Method;
            var status = ExecArgumentEncoder.TryEncode(exec.Body, method.Inputs, out var inputs);
            byte[] body;
            if (StatusCode.IsBad(status)) body = ExecResultBody.Refused(method, status);
            else
            {
                CallMethodResult result;
                try
                {
                    result = await session.CallAsync(method.ObjectId, method.MethodId, inputs, ct);
                }
                catch (Exception ex) when (IsFailure(ex, ct))
                {
                    Log?.LogError($"OPC UA call of {method.MethodIdText} on {method.ObjectIdText} failed: {ex.Message}");
                    result = new CallMethodResult { StatusCode = ex is ServiceResultException sre ? sre.StatusCode : StatusCodes.BadCommunicationError };
                }
                status = result.StatusCode;
                body = ExecResultBody.Create(method, result);
            }
            if (StatusCode.IsBad(status)) Log?.LogWarning($"Exec of {method.MethodIdText} on {method.ObjectIdText} from {exec.ReplyTo.CommandExchangeName} failed: {status}");
            else Log?.LogVerbose($"Exec {method.MethodIdText} on {method.ObjectIdText} from {exec.ReplyTo.CommandExchangeName}: {status}");
            output.SendExecResult(method.Names, body, exec.ReplyTo, exec.ReplyToRaw);
        }

        private static async Task RunAloneAsync(PendingAlone alone, CancellationToken ct)
        {
            try { await alone.Run(ct); }
            catch (Exception ex) when (IsFailure(ex, ct)) { Log?.LogError($"{alone.Description} failed: {ex.Message}"); }
        }

        /// <summary>Anything but our own cancellation is a failure of the command, never a reason to stop serving.</summary>
        private static bool IsFailure(Exception ex, CancellationToken ct) => ex is not OperationCanceledException || !ct.IsCancellationRequested;
    }
}
