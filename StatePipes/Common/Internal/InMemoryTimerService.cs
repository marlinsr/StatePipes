using StatePipes.Interfaces;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.Common.Internal
{
    /// <summary>
    /// All in-memory timers of a process component run on one dedicated thread. Entries are kept in a priority queue
    /// ordered by due time; a replaced or stopped entry is left in the queue and skipped when it surfaces.
    /// Due times are measured on the monotonic timestamp (as System.Threading.Timer is), so stepping the wall clock
    /// (NTP sync, VM resume) neither delays nor floods timers. Periodic timers that fall behind skip the missed periods.
    /// </summary>
    internal sealed class InMemoryTimerService : ITimerService, IDisposable
    {
        private sealed class Entry
        {
            public long Due;
            public long? Period;
            public IMessage? Message;
            public Action? Callback;
        }
        private readonly IMessageSender _sender;
        private readonly TimeProvider _time;
        private readonly Lock _lock = new();
        private readonly Dictionary<Guid, Entry> _entries = [];
        private readonly PriorityQueue<(Guid Id, Entry Entry), long> _queue = new();
        private readonly AutoResetEvent _signal = new(false);
        private readonly Thread _thread;
        private ITimer? _wakeTimer;
        private volatile bool _disposed;
        private bool _firing;
        /// <summary>The thread re-checks due times at least this often, so a wake timer armed against a clock that moved
        /// during arming (relative due time computed from a stale now) can never delay a timer for long.</summary>
        private static readonly TimeSpan RecheckInterval = TimeSpan.FromMilliseconds(100);
        /// <summary>Time until the earliest pending timer is due (test harness stepping); null when none.</summary>
        internal TimeSpan? TimeUntilNextDue
        {
            get
            {
                lock (_lock)
                {
                    if (_entries.Count == 0) return null;
                    var remaining = ToSpan(_entries.Values.Min(e => e.Due) - _time.GetTimestamp());
                    return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
                }
            }
        }
        /// <summary>True while a due timer has not yet been delivered (test harness idle detection).</summary>
        internal bool HasDueWork { get { lock (_lock) { var now = _time.GetTimestamp(); return _firing || _entries.Values.Any(e => e.Due <= now); } } }

        public InMemoryTimerService(IMessageSender sender, TimeProvider timeProvider)
        {
            _sender = sender;
            _time = timeProvider;
            _thread = new Thread(Run) { IsBackground = true, Name = nameof(InMemoryTimerService) };
            _thread.Start();
        }
        public void StartOneShot(Guid timerId, TimeSpan dueTime, IMessage message) => Add(timerId, new Entry { Message = message }, dueTime);
        public void StartPeriodic(Guid timerId, TimeSpan period, IMessage message)
        {
            if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(period), period, "A periodic timer needs a positive period.");
            Add(timerId, new Entry { Message = message, Period = ToTimestamp(period) }, period);
        }
        public void ScheduleCallback(TimeSpan dueTime, Action callback) => Add(Guid.NewGuid(), new Entry { Callback = callback }, dueTime);
        public void Stop(Guid timerId)
        {
            lock (_lock) { _entries.Remove(timerId); }
            _signal.Set();
        }
        public bool IsEnabled(Guid timerId)
        {
            lock (_lock) { return _entries.ContainsKey(timerId); }
        }
        private long ToTimestamp(TimeSpan span) => (long)(span.Ticks * (double)_time.TimestampFrequency / TimeSpan.TicksPerSecond);
        private TimeSpan ToSpan(long timestampDelta) => TimeSpan.FromTicks((long)(timestampDelta * (double)TimeSpan.TicksPerSecond / _time.TimestampFrequency));
        private void Add(Guid id, Entry entry, TimeSpan dueTime)
        {
            lock (_lock)
            {
                entry.Due = _time.GetTimestamp() + ToTimestamp(dueTime);
                _entries[id] = entry;
                _queue.Enqueue((id, entry), entry.Due);
            }
            _signal.Set();
        }
        private bool IsCurrent(Guid id, Entry entry, long due) =>
            _entries.TryGetValue(id, out var current) && ReferenceEquals(current, entry) && current.Due == due;
        private List<Entry> TakeDue()
        {
            List<Entry> due = [];
            lock (_lock)
            {
                var now = _time.GetTimestamp();
                long? next = null;
                while (_queue.TryPeek(out var item, out var itemDue))
                {
                    if (!IsCurrent(item.Id, item.Entry, itemDue)) { _queue.Dequeue(); continue; }
                    if (itemDue > now) { next = itemDue; break; }
                    _queue.Dequeue();
                    due.Add(item.Entry);
                    if (item.Entry.Period is long period)
                    {
                        // Next slot on the original cadence strictly after now: no drift, and missed periods are skipped.
                        item.Entry.Due = itemDue + period * ((now - itemDue) / period + 1);
                        _queue.Enqueue(item, item.Entry.Due);
                    }
                    else _entries.Remove(item.Id);
                }
                _firing = due.Count > 0;
                _wakeTimer?.Dispose();
                _wakeTimer = next is long n ? _time.CreateTimer(_ => _signal.Set(), null, ToSpan(n - now), Timeout.InfiniteTimeSpan) : null;
                // The clock may have moved while the wake timer was armed relative to the stale 'now'; re-check after arming.
                if (next is long nextDue && _time.GetTimestamp() >= nextDue) _signal.Set();
            }
            return due;
        }
        private void Run()
        {
            while (!_disposed)
            {
                foreach (var entry in TakeDue())
                {
                    try
                    {
                        if (entry.Callback != null) entry.Callback();
                        else if (entry.Message != null) _sender.SendMessage(entry.Message);
                    }
                    catch (Exception ex) { Log?.LogException(ex); }
                }
                lock (_lock) _firing = false;
                _signal.WaitOne(RecheckInterval);
            }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _signal.Set();
            lock (_lock) { _wakeTimer?.Dispose(); }
        }
    }
}
