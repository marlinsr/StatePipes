using StatePipes.Interfaces;

namespace StatePipes.ProcessLevelServices
{
    public class LoggerConfiguration : IConfigSettings
    {
        public int MaxUnflushedLogStatements { get; set; }
        public int FlushHighWaterMark { get; set; }
        public ILogger.LogLevel InitialLogLevel { get; set; }
        public int FlushTimeoutMilliseconds { get; set; }
        public int NumberOfLogFilesToKeep { get; set; }
        public int MaxLinesPerFile { get; set; }
        public object GetDefaults()
        {
            return new LoggerConfiguration() { MaxUnflushedLogStatements = 1000
                , FlushHighWaterMark = 500
                , InitialLogLevel = ILogger.LogLevel.Info
                , FlushTimeoutMilliseconds = 1000
                , NumberOfLogFilesToKeep = 10
                , MaxLinesPerFile = 2000
            };
        }
    }
}
