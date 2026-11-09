namespace StatePipes.Workflow.Internal
{
    /// <summary>Workflow store for tests: same contract and transaction semantics as the PostgreSQL store.</summary>
    internal sealed class InMemoryWorkflowStore(TimeProvider timeProvider) : IWorkflowStore
    {
        private sealed record TimerEntry(TimerRecord Record, DateTimeOffset Expiration);
        private readonly Lock _lock = new();
        private readonly Dictionary<(string, string), WorkflowRecord> _instances = [];
        private readonly Dictionary<Guid, TimerEntry> _timers = [];
        private readonly HashSet<(string, string)> _held = [];
        private readonly HashSet<(string, string)> _simulated = [];

        public IReadOnlyCollection<WorkflowRecord> Instances { get { lock (_lock) return [.. _instances.Values]; } }
        public IReadOnlyCollection<TimerRecord> Timers { get { lock (_lock) return [.. _timers.Values.Select(t => t.Record)]; } }
        public DateTimeOffset? ExpirationOf(Guid timerId) { lock (_lock) return _timers.TryGetValue(timerId, out var t) ? t.Expiration : null; }
        public DateTimeOffset? NextExpiration(IReadOnlyCollection<string> workflowTypes)
        {
            lock (_lock)
            {
                var pending = _timers.Values.Where(t => workflowTypes.Contains(t.Record.WorkflowType)).ToList();
                return pending.Count == 0 ? null : pending.Min(t => t.Expiration);
            }
        }
        public void SimulateLock(string workflowType, string correlationId) { lock (_lock) _simulated.Add((workflowType, correlationId)); }
        public void ReleaseSimulatedLock(string workflowType, string correlationId) { lock (_lock) _simulated.Remove((workflowType, correlationId)); }
        /// <summary>Seeds a committed instance row directly (tests of corrupt or legacy data).</summary>
        public void Seed(WorkflowRecord record) { lock (_lock) _instances[(record.WorkflowType, record.CorrelationId)] = record; }

        private DateTimeOffset Now => timeProvider.GetUtcNow();
        public void EnsureSchema() { }
        public IWorkflowUnitOfWork Begin() => new UnitOfWork(this);

        public int FireDueTimers(IReadOnlyCollection<string> workflowTypes, int batchSize, Func<TimerRecord, bool> send)
        {
            lock (_lock)
            {
                var now = timeProvider.GetUtcNow();
                var due = _timers.Values.Where(t => workflowTypes.Contains(t.Record.WorkflowType) && t.Expiration <= now)
                    .OrderBy(t => t.Expiration).Take(batchSize).ToList();
                int fired = 0;
                foreach (var entry in due)
                {
                    if (!send(entry.Record)) continue;
                    fired++;
                    if (entry.Record.Period is TimeSpan period)
                        _timers[entry.Record.TimerId] = entry with { Expiration = PeriodicSchedule.NextDue(entry.Expiration, period, now) };
                    else _timers.Remove(entry.Record.TimerId);
                }
                return fired;
            }
        }

        private sealed class UnitOfWork(InMemoryWorkflowStore store) : IWorkflowUnitOfWork
        {
            private readonly List<(string, string)> _locks = [];
            private readonly Dictionary<(string, string), WorkflowRecord?> _instances = [];
            private readonly Dictionary<Guid, TimerEntry?> _timers = [];
            private bool _done;

            public bool TryLock(string workflowType, string correlationId)
            {
                var key = (workflowType, correlationId);
                lock (store._lock)
                {
                    if (_locks.Contains(key)) return true;
                    if (store._simulated.Contains(key) || !store._held.Add(key)) return false;
                    _locks.Add(key);
                    return true;
                }
            }
            public WorkflowRecord? Load(string workflowType, string correlationId)
            {
                var key = (workflowType, correlationId);
                if (_instances.TryGetValue(key, out var staged)) return staged;
                lock (store._lock) return store._instances.GetValueOrDefault(key);
            }
            public void Upsert(string workflowType, string correlationId, string currentState, string documentJson)
            {
                var version = (Load(workflowType, correlationId)?.Version ?? 0) + 1;
                _instances[(workflowType, correlationId)] = new WorkflowRecord(workflowType, correlationId, currentState, documentJson, version);
            }
            public void DeleteInstanceAndTimers(string workflowType, string correlationId)
            {
                _instances[(workflowType, correlationId)] = null;
                List<Guid> ids;
                lock (store._lock)
                    ids = [.. store._timers.Values.Where(t => t.Record.WorkflowType == workflowType && t.Record.CorrelationId == correlationId).Select(t => t.Record.TimerId)];
                ids.AddRange(_timers.Where(t => t.Value?.Record.WorkflowType == workflowType && t.Value.Record.CorrelationId == correlationId).Select(t => t.Key));
                foreach (var id in ids) _timers[id] = null;
            }
            public void UpsertTimer(TimerRecord timer, TimeSpan dueTime) =>
                _timers[timer.TimerId] = new TimerEntry(timer, store.Now + dueTime);
            public void DeleteTimer(Guid timerId) => _timers[timerId] = null;
            public bool TimerExists(Guid timerId)
            {
                if (_timers.TryGetValue(timerId, out var staged)) return staged != null;
                lock (store._lock) return store._timers.ContainsKey(timerId);
            }
            public void Commit()
            {
                lock (store._lock)
                {
                    foreach (var (key, rec) in _instances)
                        if (rec == null) store._instances.Remove(key); else store._instances[key] = rec;
                    foreach (var (id, entry) in _timers)
                        if (entry == null) store._timers.Remove(id); else store._timers[id] = entry;
                }
                _done = true;
                Dispose();
            }
            public void Dispose()
            {
                lock (store._lock) foreach (var key in _locks) store._held.Remove(key);
                _locks.Clear();
                if (!_done) { _instances.Clear(); _timers.Clear(); }
            }
        }
    }
}
