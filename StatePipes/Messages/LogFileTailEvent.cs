using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    public class LogFileTailEvent(string logFileTail) : IEvent
    {
        public string LogFileTail { get; } = logFileTail;
    }
}
