using StatePipes.Interfaces;

namespace StatePipes.Common.Internal
{
    internal interface ITimerService
    {
        void StartOneShot(Guid timerId, TimeSpan dueTime, IMessage message);
        void StartPeriodic(Guid timerId, TimeSpan period, IMessage message);
        void Stop(Guid timerId);
        bool IsEnabled(Guid timerId);
    }
}
