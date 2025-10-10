using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    public class LogLevelEvent(ILogger.LogLevel logLevel) : IEvent
    {
        public ILogger.LogLevel LogLevel { get; } = logLevel;
    }
}
