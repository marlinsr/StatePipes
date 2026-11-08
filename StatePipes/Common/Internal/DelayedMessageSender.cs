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
        public void StartPeriodic(TimeSpan period, TMessage message) => timers.StartPeriodic(TimerId, period, message);
        public void Stop() => timers.Stop(TimerId);
        public void Dispose() => Stop();
    }
}
