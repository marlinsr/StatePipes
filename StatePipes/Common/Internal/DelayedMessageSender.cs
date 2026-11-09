using Newtonsoft.Json;
using StatePipes.Interfaces;

namespace StatePipes.Common.Internal
{
    /// <summary>A named handle onto a timer owned by an <see cref="ITimerService"/>; it holds no timer of its own.</summary>
    internal class DelayedMessageSender<TMessage>(ITimerService timers, Guid timerId) : IDelayedMessageSender<TMessage>, IDisposable where TMessage : class, IMessage
    {
        public Guid TimerId { get; } = timerId;
        [JsonIgnore]
        public bool Enabled => timers.IsEnabled(TimerId);
        public void StartOneShot(TimeSpan dueTime, TMessage message) => timers.StartOneShot(TimerId, dueTime, message);
        /// <summary>Like System.Threading.Timer: a zero period fires once (immediately); a negative period is rejected.</summary>
        public void StartPeriodic(TimeSpan period, TMessage message)
        {
            if (period < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(period), period, "The period cannot be negative.");
            if (period == TimeSpan.Zero) timers.StartOneShot(TimerId, TimeSpan.Zero, message);
            else timers.StartPeriodic(TimerId, period, message);
        }
        public void Stop() => timers.Stop(TimerId);
        public void Dispose() => Stop();
    }
}
