namespace StatePipes.StateMachine.Test.Internal
{
    /// <summary>A clock that only moves when told to; timers fire synchronously as it passes their due time.</summary>
    internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private readonly Lock _lock = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() { lock (_lock) return _now; }
        public override long GetTimestamp() { lock (_lock) return _now.UtcTicks; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void SetUtcNow(DateTimeOffset value)
        {
            List<ManualTimer> due;
            lock (_lock)
            {
                if (value > _now) _now = value;
                due = [.. _timers.Where(t => t.Due <= _now)];
                foreach (var t in due) _timers.Remove(t);
            }
            foreach (var t in due) t.Fire();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset Due;
            public void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._lock)
                {
                    owner._timers.Remove(this);
                    if (dueTime == Timeout.InfiniteTimeSpan) return true;
                    Due = owner._now + dueTime;
                    owner._timers.Add(this);
                }
                if (dueTime <= TimeSpan.Zero) owner.SetUtcNow(owner.GetUtcNow());
                return true;
            }
            public void Dispose() { lock (owner._lock) owner._timers.Remove(this); }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    /// <summary>Fallback so every command type resolves in the test bus and is recorded, even with no real handler.</summary>
    internal sealed class NoOpMessageHandler<TMessage> : StatePipes.Interfaces.IMessageHandler<TMessage> where TMessage : class
    {
        public void HandleMessage(TMessage message, StatePipes.Comms.BusConfig? responseInfo, bool isResponse) { }
    }
}
