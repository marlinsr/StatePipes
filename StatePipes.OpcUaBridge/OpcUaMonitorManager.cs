using Opc.Ua;
using Opc.Ua.Client;
using StatePipes.OpcUaBridge.Messages;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Turns monitor definitions into OPC UA monitored items on one session. Publishing interval belongs to a
    /// subscription, not an item, so there is one subscription per distinct interval, created on first use and
    /// deleted when its last item goes. Every item carries a DataChangeFilter built from its definition, so the
    /// server never falls back to defaults of its own.
    /// </summary>
    internal sealed class OpcUaMonitorManager(ISession session, ITelemetryContext telemetry, Action<MonitorKey, DataValue> onNotification) : IMonitorSession
    {
        private const uint KeepAliveCount = 10;
        private const uint LifetimeCount = 100;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<double, Subscription> _subscriptions = [];
        private readonly Dictionary<MonitorKey, Entry> _entries = [];

        private sealed record Entry(MonitoredItem Item, Subscription Subscription, string NodeIdText, double PublishingInterval);

        public async Task<MonitorApplyResult> ApplyAsync(MonitorDefinition definition, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (_entries.TryGetValue(definition.Key, out var entry))
                {
                    if (entry.NodeIdText == definition.NodeIdText && entry.PublishingInterval == definition.Parameters.PublishingInterval)
                        return await ModifyAsync(entry, definition.Parameters, ct);
                    await RemoveEntryAsync(definition.Key, entry, ct);
                }
                return (await CreateAsync(definition.Parameters.PublishingInterval, [definition], ct))[definition.Key];
            }
            finally { _gate.Release(); }
        }

        public async Task<IReadOnlyDictionary<MonitorKey, MonitorApplyResult>> ApplyAllAsync(IReadOnlyList<MonitorDefinition> definitions, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            try
            {
                Dictionary<MonitorKey, MonitorApplyResult> results = [];
                foreach (var key in definitions.Select(d => d.Key).Where(_entries.ContainsKey).ToList()) await RemoveEntryAsync(key, _entries[key], ct);
                foreach (var group in definitions.GroupBy(d => d.Parameters.PublishingInterval))
                    foreach (var (key, result) in await CreateAsync(group.Key, [.. group], ct)) results[key] = result;
                return results;
            }
            finally { _gate.Release(); }
        }

        public async Task RemoveAsync(MonitorKey key, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (_entries.TryGetValue(key, out var entry)) await RemoveEntryAsync(key, entry, ct);
            }
            finally { _gate.Release(); }
        }

        public async Task DisableAsync(MonitorKey key, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (_entries.TryGetValue(key, out var entry)) await entry.Subscription.SetMonitoringModeAsync(Opc.Ua.MonitoringMode.Disabled, [entry.Item], ct);
            }
            finally { _gate.Release(); }
        }

        /// <summary>
        /// Creates monitored items in one subscription with a single request. Items the server refuses are
        /// dropped from the subscription again and reported Failed; when the request itself fails, every item is
        /// Pending and the next session tries again.
        /// </summary>
        private async Task<Dictionary<MonitorKey, MonitorApplyResult>> CreateAsync(double publishingInterval, IReadOnlyList<MonitorDefinition> definitions, CancellationToken ct)
        {
            Dictionary<MonitorKey, MonitorApplyResult> results = [];
            List<(MonitorDefinition Definition, MonitoredItem Item)> created = [];
            foreach (var definition in definitions)
            {
                if (!TryParseNodeId(definition.NodeIdText, out var nodeId)) results[definition.Key] = MonitorApplyResult.Failed(StatusCodes.BadNodeIdInvalid);
                else created.Add((definition, CreateItem(definition, nodeId)));
            }
            if (created.Count == 0) return results;
            Subscription subscription;
            try
            {
                subscription = await GetSubscriptionAsync(publishingInterval, ct);
                subscription.AddItems(created.Select(c => c.Item));
                await subscription.ApplyChangesAsync(ct);
            }
            catch (ServiceResultException ex)
            {
                Log?.LogError($"OPC UA could not create {created.Count} monitored items: {ex.Message}");
                foreach (var (definition, item) in created)
                {
                    item.Subscription?.RemoveItem(item);
                    results[definition.Key] = MonitorApplyResult.Pending;
                }
                await DeleteIfEmptyAsync(publishingInterval, ct);
                return results;
            }
            foreach (var (definition, item) in created)
            {
                var error = item.Status.Error;
                if (!item.Created || (error != null && ServiceResult.IsBad(error)))
                {
                    subscription.RemoveItem(item);
                    results[definition.Key] = MonitorApplyResult.Failed(error?.StatusCode ?? StatusCodes.BadUnexpectedError);
                    continue;
                }
                _entries[definition.Key] = new Entry(item, subscription, definition.NodeIdText, publishingInterval);
                results[definition.Key] = Active(item, subscription);
            }
            await DeleteIfEmptyAsync(publishingInterval, ct);
            return results;
        }

        private async Task<MonitorApplyResult> ModifyAsync(Entry entry, MonitorParameters parameters, CancellationToken ct)
        {
            var item = entry.Item;
            item.SamplingInterval = (int)parameters.SamplingInterval;
            item.QueueSize = parameters.QueueSize;
            item.Filter = CreateFilter(parameters);
            try
            {
                await entry.Subscription.ApplyChangesAsync(ct);
                var mode = ToMonitoringMode(parameters.MonitoringMode);
                if (item.Status.MonitoringMode != mode) await entry.Subscription.SetMonitoringModeAsync(mode, [item], ct);
            }
            catch (ServiceResultException ex)
            {
                Log?.LogError($"OPC UA could not modify monitored item on {entry.NodeIdText}: {ex.Message}");
                return MonitorApplyResult.Pending;
            }
            var error = item.Status.Error;
            return error != null && ServiceResult.IsBad(error) ? MonitorApplyResult.Failed(error.StatusCode) : Active(item, entry.Subscription);
        }

        private async Task RemoveEntryAsync(MonitorKey key, Entry entry, CancellationToken ct)
        {
            _entries.Remove(key);
            entry.Subscription.RemoveItem(entry.Item);
            try { await entry.Subscription.ApplyChangesAsync(ct); }
            catch (ServiceResultException ex) { Log?.LogVerbose($"OPC UA could not delete monitored item on {entry.NodeIdText}: {ex.Message}"); }
            await DeleteIfEmptyAsync(entry.PublishingInterval, ct);
        }

        private async Task<Subscription> GetSubscriptionAsync(double publishingInterval, CancellationToken ct)
        {
            if (_subscriptions.TryGetValue(publishingInterval, out var existing)) return existing;
            Subscription subscription = new(telemetry, new SubscriptionOptions
            {
                DisplayName = $"StatePipes {publishingInterval} ms",
                PublishingInterval = (int)publishingInterval,
                PublishingEnabled = true,
                KeepAliveCount = KeepAliveCount,
                LifetimeCount = LifetimeCount,
                TimestampsToReturn = TimestampsToReturn.Both
            });
            session.AddSubscription(subscription);
            try { await subscription.CreateAsync(ct); }
            catch
            {
                await session.RemoveSubscriptionAsync(subscription, ct);
                throw;
            }
            _subscriptions[publishingInterval] = subscription;
            return subscription;
        }

        private async Task DeleteIfEmptyAsync(double publishingInterval, CancellationToken ct)
        {
            if (!_subscriptions.TryGetValue(publishingInterval, out var subscription) || subscription.MonitoredItemCount > 0) return;
            _subscriptions.Remove(publishingInterval);
            try { await session.RemoveSubscriptionAsync(subscription, ct); }
            catch (ServiceResultException ex) { Log?.LogVerbose($"OPC UA could not delete subscription: {ex.Message}"); }
        }

        private MonitoredItem CreateItem(MonitorDefinition definition, NodeId nodeId)
        {
            var parameters = definition.Parameters;
            MonitoredItem item = new(telemetry, new MonitoredItemOptions
            {
                DisplayName = definition.Key.MonitorName,
                StartNodeId = nodeId,
                AttributeId = Attributes.Value,
                MonitoringMode = ToMonitoringMode(parameters.MonitoringMode),
                SamplingInterval = (int)parameters.SamplingInterval,
                QueueSize = parameters.QueueSize,
                DiscardOldest = true,
                Filter = CreateFilter(parameters)
            })
            { Handle = definition.Key };
            item.Notification += OnNotification;
            return item;
        }

        private void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            if (item.Handle is not MonitorKey key || e.NotificationValue is not MonitoredItemNotification notification) return;
            try { onNotification(key, notification.Value); }
            catch (Exception ex) { Log?.LogException(ex); }
        }

        private static MonitorApplyResult Active(MonitoredItem item, Subscription subscription) =>
            new(MonitorState.Active, StatusCodes.Good, item.Status.SamplingInterval, item.Status.QueueSize, subscription.CurrentPublishingInterval);

        private static DataChangeFilter CreateFilter(MonitorParameters parameters) => new()
        {
            Trigger = (DataChangeTrigger)(int)parameters.DataChangeTrigger,
            DeadbandType = (uint)parameters.DeadbandType,
            DeadbandValue = parameters.DeadbandType == DeadbandTypeKind.None ? 0 : parameters.DeadbandValue
        };

        private static Opc.Ua.MonitoringMode ToMonitoringMode(MonitoringModeKind mode) =>
            mode == MonitoringModeKind.Reporting ? Opc.Ua.MonitoringMode.Reporting : Opc.Ua.MonitoringMode.Disabled;

        private static bool TryParseNodeId(string text, out NodeId nodeId)
        {
            try
            {
                nodeId = NodeId.Parse(text);
                return !nodeId.IsNull;
            }
            catch (ServiceResultException)
            {
                nodeId = NodeId.Null;
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync();
            try
            {
                foreach (var subscription in _subscriptions.Values)
                {
                    try { await session.RemoveSubscriptionAsync(subscription, CancellationToken.None); }
                    catch (Exception ex) when (ex is ServiceResultException or ObjectDisposedException) { }
                }
                _subscriptions.Clear();
                _entries.Clear();
            }
            finally { _gate.Release(); }
        }
    }
}
