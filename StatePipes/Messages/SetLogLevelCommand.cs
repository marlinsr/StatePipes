using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    public class SetLogLevelCommand(ILogger.LogLevel logLevel) : ICommand
    {
        public ILogger.LogLevel LogLevel { get; set; } = logLevel;
    }
}
