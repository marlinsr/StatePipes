using StatePipes.Interfaces;
using StatePipes.ProcessLevelServices;
using System.Collections.Concurrent;

namespace StatePipes.Test.Workflow
{
    /// <summary>Captures log lines for assertions; installed via LoggerHolder.SetLoggerForTest.</summary>
    internal sealed class RecordingLogger : ILogger, IDisposable
    {
        private readonly ILogger? _previous;
        public readonly ConcurrentQueue<(ILogger.LogLevel Level, string Message)> Lines = new();
        public RecordingLogger() => _previous = LoggerHolder.SetLoggerForTest(this);
        public void Dispose() => LoggerHolder.SetLoggerForTest(_previous);
        public IEnumerable<string> At(ILogger.LogLevel level) => Lines.Where(l => l.Level == level).Select(l => l.Message);
        public bool Contains(string text) => Lines.Any(l => l.Message.Contains(text));

        public void Start() { }
        public void Stop() { }
        public void SetLogLevel(ILogger.LogLevel logLevel) { }
        public ILogger.LogLevel GetLogLevel() => ILogger.LogLevel.Verbose;
        public void Flush(CancellationToken token, int timeoutMilliseconds = Timeout.Infinite) { }
        public void Flush(int timeoutMilliseconds = Timeout.Infinite) { }
        public void LogVerbose(string message, string filePath = "", int lineNumber = 0, string memberName = "") => Lines.Enqueue((ILogger.LogLevel.Verbose, message));
        public void LogInfo(string message, string filePath = "", int lineNumber = 0, string memberName = "") => Lines.Enqueue((ILogger.LogLevel.Info, message));
        public void LogWarning(string message, string filePath = "", int lineNumber = 0, string memberName = "") => Lines.Enqueue((ILogger.LogLevel.Warning, message));
        public void LogError(string message, string filePath = "", int lineNumber = 0, string memberName = "") => Lines.Enqueue((ILogger.LogLevel.Error, message));
        public void LogException(Exception ex, string filePath = "", int lineNumber = 0, string memberName = "") => Lines.Enqueue((ILogger.LogLevel.Error, ex.ToString()));
    }
}
