using StatePipes.Comms;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Every monitor clients have asked for: the single source of truth the bridge rebuilds OPC UA monitored
    /// items from. Each change is written straight to the <see cref="MonitorStore"/>; when the write fails the
    /// registry stays authoritative and the next change writes everything again.
    /// </summary>
    internal sealed class MonitorRegistry
    {
        private readonly object _lock = new();
        private readonly Dictionary<MonitorKey, MonitorDefinition> _monitors = [];
        private readonly MonitorStore _store;
        private readonly TimeSpan? _suspendedExpiry;
        private readonly TimeProvider _time;

        private MonitorRegistry(MonitorStore store, TimeSpan? suspendedExpiry, TimeProvider time)
        {
            _store = store;
            _suspendedExpiry = suspendedExpiry;
            _time = time;
        }

        /// <summary>
        /// Loads the store, dropping monitors this bridge may not answer: a file written under another broker or
        /// certificate would otherwise send responses the guards refuse forever.
        /// </summary>
        public static MonitorRegistry Load(MonitorStore store, BusConfig own, TimeSpan? suspendedExpiry, TimeProvider time)
        {
            MonitorRegistry registry = new(store, suspendedExpiry, time);
            var dropped = false;
            foreach (var definition in store.Load())
            {
                var replyTo = definition.ReplyTo;
                string reason = "its reply-to is not a BusConfig";
                if (replyTo == null || !ReplyToGuard.Allows(own, replyTo, out reason))
                {
                    Log?.LogWarning($"Dropping monitor on {definition.NodeIdText} of {definition.Key.SenderCommandExchange} from {store.Path}: {reason}");
                    dropped = true;
                    continue;
                }
                registry._monitors[definition.Key] = definition;
            }
            if (registry.RemoveExpired().Count == 0 && dropped) registry.Save();
            return registry;
        }

        public MonitorDefinition? Get(MonitorKey key)
        {
            lock (_lock) return _monitors.GetValueOrDefault(key);
        }

        public IReadOnlyList<MonitorDefinition> GetAll()
        {
            lock (_lock) return [.. _monitors.Values];
        }

        /// <summary>Adds or replaces a monitor, live again if it was suspended. Returns what it replaced.</summary>
        public MonitorDefinition? Upsert(MonitorDefinition definition)
        {
            lock (_lock)
            {
                var previous = _monitors.GetValueOrDefault(definition.Key);
                _monitors[definition.Key] = definition with { SuspendedSince = null };
                Save();
                return previous;
            }
        }

        public bool Remove(MonitorKey key)
        {
            lock (_lock)
            {
                if (!_monitors.Remove(key)) return false;
                Save();
                return true;
            }
        }

        public void Suspend(MonitorKey key)
        {
            lock (_lock)
            {
                if (!_monitors.TryGetValue(key, out var definition) || definition.IsSuspended) return;
                _monitors[key] = definition with { SuspendedSince = _time.GetUtcNow().UtcDateTime };
                Save();
            }
        }

        /// <summary>Removes monitors suspended for longer than the configured expiry, returning their keys.</summary>
        public IReadOnlyList<MonitorKey> RemoveExpired()
        {
            if (_suspendedExpiry is not { } expiry) return [];
            lock (_lock)
            {
                var cutoff = _time.GetUtcNow().UtcDateTime - expiry;
                List<MonitorKey> expired = [.. _monitors.Values.Where(d => d.SuspendedSince < cutoff).Select(d => d.Key)];
                if (expired.Count == 0) return expired;
                foreach (var key in expired) _monitors.Remove(key);
                Log?.LogInfo($"Removed {expired.Count} monitors suspended for longer than {expiry}");
                Save();
                return expired;
            }
        }

        private void Save() => _store.Save([.. _monitors.Values]);
    }
}
