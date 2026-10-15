using Opc.Ua;
using StatePipes.OpcUaBridge.Messages;
using System.Threading.Channels;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Owns the monitor lifecycle. Every change -- Monitor and Delete commands, a new or lost OPC UA session,
    /// reply-to checks and expiry -- is queued and handled by one reader, so a command can never race a rebuild
    /// or touch a subscription that is being torn down. Commands update the <see cref="MonitorRegistry"/>
    /// first, then the live session if there is one.
    ///
    /// <para>Notifications are not queued: they arrive on the OPC UA stack's thread and go straight out to the
    /// monitor's current reply-to, unless it is suspended.</para>
    /// </summary>
    internal sealed class MonitorCoordinator(MonitorRegistry registry, IMonitorOutput output, TimeProvider time)
    {
        public static readonly TimeSpan ReplyToCheckInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan ExpiryCheckInterval = TimeSpan.FromHours(1);

        private readonly Channel<MonitorWork> _work = Channel.CreateUnbounded<MonitorWork>(new UnboundedChannelOptions { SingleReader = true });
        private volatile IReadOnlyDictionary<string, OpcUaDataItem> _catalog = new Dictionary<string, OpcUaDataItem>();
        private IMonitorSession? _session;

        private abstract record MonitorWork;
        private sealed record MonitorRequest(MonitorKey Key, OpcUaDataItem Item, MonitorParameters Parameters, string ReplyToRaw) : MonitorWork;
        private sealed record DeleteRequest(MonitorKey Key, string ReplyToRaw) : MonitorWork;
        private sealed record SessionStart(IMonitorSession Session, IReadOnlyList<OpcUaDataItem> Catalog) : MonitorWork;
        private sealed record SessionEnd : MonitorWork;
        private sealed record ReplyToCheck : MonitorWork;
        private sealed record ExpiryCheck : MonitorWork;

        public void Monitor(MonitorKey key, OpcUaDataItem item, MonitorParameters parameters, string replyToRaw) => Enqueue(new MonitorRequest(key, item, parameters, replyToRaw));
        public void Delete(MonitorKey key, string replyToRaw) => Enqueue(new DeleteRequest(key, replyToRaw));
        public void SessionStarted(IMonitorSession session, IReadOnlyList<OpcUaDataItem> catalog) => Enqueue(new SessionStart(session, catalog));
        public void SessionLost() => Enqueue(new SessionEnd());
        public void CheckReplyTos() => Enqueue(new ReplyToCheck());

        private void Enqueue(MonitorWork work) => _work.Writer.TryWrite(work);

        public void OnNotification(MonitorKey key, DataValue value)
        {
            var definition = registry.Get(key);
            if (definition == null || definition.IsSuspended || !_catalog.TryGetValue(definition.NodeIdText, out var item)) return;
            output.SendNotification(item, value, definition.ReplyToRaw);
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var replyToChecks = EnqueuePeriodicallyAsync(ReplyToCheckInterval, new ReplyToCheck(), ct);
            var expiryChecks = EnqueuePeriodicallyAsync(ExpiryCheckInterval, new ExpiryCheck(), ct);
            try
            {
                await foreach (var work in _work.Reader.ReadAllAsync(ct))
                {
                    try { await HandleAsync(work, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { Log?.LogException(ex); }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                await Task.WhenAll(replyToChecks, expiryChecks);
                await EndSessionAsync();
            }
        }

        private async Task EnqueuePeriodicallyAsync(TimeSpan interval, MonitorWork work, CancellationToken ct)
        {
            using PeriodicTimer timer = new(interval, time);
            try
            {
                while (await timer.WaitForNextTickAsync(ct)) Enqueue(work);
            }
            catch (OperationCanceledException) { }
        }

        private Task HandleAsync(MonitorWork work, CancellationToken ct) => work switch
        {
            MonitorRequest request => HandleMonitorAsync(request, ct),
            DeleteRequest request => HandleDeleteAsync(request, ct),
            SessionStart start => HandleSessionStartAsync(start, ct),
            SessionEnd => EndSessionAsync(),
            ReplyToCheck => HandleReplyToCheckAsync(ct),
            ExpiryCheck => RemoveExpiredAsync(ct),
            _ => Task.CompletedTask
        };

        private async Task HandleMonitorAsync(MonitorRequest request, CancellationToken ct)
        {
            MonitorDefinition definition = new(request.Key, request.Item.NodeIdText, request.Parameters, request.ReplyToRaw, null);
            registry.Upsert(definition);
            var result = await ApplyAsync(definition, ct);
            SendStatus(definition, result.State, result);
        }

        private async Task<MonitorApplyResult> ApplyAsync(MonitorDefinition definition, CancellationToken ct)
        {
            if (_session == null) return MonitorApplyResult.Pending;
            try { return await _session.ApplyAsync(definition, ct); }
            catch (Exception ex) when (ex is ServiceResultException or InvalidOperationException or ObjectDisposedException)
            {
                Log?.LogError($"Could not apply monitor {definition.Key.MonitorName} on {definition.NodeIdText}: {ex.Message}");
                return MonitorApplyResult.Pending;
            }
        }

        private async Task HandleDeleteAsync(DeleteRequest request, CancellationToken ct)
        {
            var definition = registry.Get(request.Key);
            if (definition == null || !registry.Remove(request.Key))
            {
                output.SendStatus(Status(request.Key.MonitorName, string.Empty, MonitorState.Failed, MonitorApplyResult.Failed(StatusCodes.BadNotFound)), request.ReplyToRaw);
                return;
            }
            await RemoveFromSessionAsync(request.Key, ct);
            output.SendStatus(Status(request.Key.MonitorName, definition.NodeIdText, MonitorState.Deleted, new MonitorApplyResult(MonitorState.Deleted, StatusCodes.Good, 0, 0, 0)), request.ReplyToRaw);
        }

        /// <summary>
        /// A new session starts with no monitored items, so every live monitor is created again and its sender
        /// told how that went. Suspended monitors stay out of the session until reclaimed.
        /// </summary>
        private async Task HandleSessionStartAsync(SessionStart start, CancellationToken ct)
        {
            await EndSessionAsync();
            _session = start.Session;
            var catalog = start.Catalog.ToDictionary(item => item.NodeIdText, StringComparer.Ordinal);
            _catalog = catalog;
            registry.RemoveExpired();
            var live = registry.GetAll().Where(d => !d.IsSuspended).ToList();
            var unknown = live.Where(d => !catalog.ContainsKey(d.NodeIdText)).ToList();
            var known = live.Except(unknown).ToList();
            IReadOnlyDictionary<MonitorKey, MonitorApplyResult> results;
            try { results = await start.Session.ApplyAllAsync(known, ct); }
            catch (Exception ex) when (ex is ServiceResultException or InvalidOperationException or ObjectDisposedException)
            {
                Log?.LogError($"Could not rebuild {known.Count} monitors: {ex.Message}");
                results = known.ToDictionary(d => d.Key, _ => MonitorApplyResult.Pending);
            }
            Log?.LogInfo($"Rebuilt {results.Count(r => r.Value.State == MonitorState.Active)} of {live.Count} monitors on the new OPC UA session");
            foreach (var definition in unknown)
            {
                Log?.LogWarning($"Monitor {definition.Key.MonitorName} of {definition.Key.SenderCommandExchange}: {definition.NodeIdText} is no longer on the server");
                SendStatus(definition, MonitorState.Failed, MonitorApplyResult.Failed(StatusCodes.BadNodeIdUnknown));
            }
            foreach (var definition in known)
            {
                var result = results.GetValueOrDefault(definition.Key, MonitorApplyResult.Pending);
                SendStatus(definition, result.State, result);
            }
        }

        private async Task EndSessionAsync()
        {
            var session = _session;
            _session = null;
            if (session == null) return;
            try { await session.DisposeAsync(); }
            catch (Exception ex) { Log?.LogVerbose($"Closing monitor session: {ex.Message}"); }
        }

        /// <summary>
        /// Suspends every monitor whose reply-to bus has gone, typically because its client disconnected and its
        /// auto-delete response exchange went with it. Each distinct reply-to is checked once.
        /// </summary>
        private async Task HandleReplyToCheckAsync(CancellationToken ct)
        {
            foreach (var group in registry.GetAll().Where(d => !d.IsSuspended).GroupBy(d => d.ReplyToRaw, StringComparer.Ordinal))
            {
                var replyTo = group.First().ReplyTo;
                if (replyTo == null) continue;
                bool exists;
                try { exists = await output.ReplyToExistsAsync(replyTo, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log?.LogVerbose($"Could not check {replyTo.ResponseExchangeName}: {ex.Message}");
                    continue;
                }
                if (exists) continue;
                Log?.LogInfo($"Suspending {group.Count()} monitors: {replyTo.ResponseExchangeName} no longer exists");
                foreach (var definition in group)
                {
                    if (_session != null)
                    {
                        try { await _session.DisableAsync(definition.Key, ct); }
                        catch (Exception ex) when (ex is ServiceResultException or ObjectDisposedException) { Log?.LogVerbose($"Could not disable monitor {definition.Key.MonitorName}: {ex.Message}"); }
                    }
                    registry.Suspend(definition.Key);
                }
            }
        }

        private async Task RemoveExpiredAsync(CancellationToken ct)
        {
            foreach (var key in registry.RemoveExpired()) await RemoveFromSessionAsync(key, ct);
        }

        private async Task RemoveFromSessionAsync(MonitorKey key, CancellationToken ct)
        {
            if (_session == null) return;
            try { await _session.RemoveAsync(key, ct); }
            catch (Exception ex) when (ex is ServiceResultException or ObjectDisposedException) { Log?.LogVerbose($"Could not remove monitor {key.MonitorName}: {ex.Message}"); }
        }

        private void SendStatus(MonitorDefinition definition, MonitorState state, MonitorApplyResult result) =>
            output.SendStatus(Status(definition.Key.MonitorName, definition.NodeIdText, state, result), definition.ReplyToRaw);

        private static MonitorStatusEvent Status(string name, string nodeId, MonitorState state, MonitorApplyResult result) =>
            new(name, nodeId, state, result.StatusCode.Code, result.StatusCode.ToString(), result.RevisedSamplingInterval, result.RevisedQueueSize, result.RevisedPublishingInterval);
    }
}
