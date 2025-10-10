using StatePipes.Interfaces;
using System.Runtime.CompilerServices;
namespace StatePipes.ProcessLevelServices.Internal
{
    internal class Logger  : ILogger
    {
        internal LoggerTask _task;
        internal ILogger.LogLevel _logLevel;
        internal bool _stopping;
        public Logger()
        {
            _task = new();
            _logLevel = _task.Configuration.InitialLogLevel;
        }        
        public void Start()
        {
            _task.StartLongRunningAndWait();
        }
        public void Stop()
        {
            _stopping = true;
            while (_task.GetNumberOfItemsQueued() > 0)
            {
                Flush();
                Thread.Yield();
            }
            _task.Cancel();
        }
        public void SetLogLevel(ILogger.LogLevel logLevel) => _logLevel = logLevel;
        public ILogger.LogLevel GetLogLevel() => _logLevel;
        public void Flush(CancellationToken token, int timeoutMilliseconds = Timeout.Infinite) => _task.Flush(token, timeoutMilliseconds);
        public void Flush(int timeoutMilliseconds = Timeout.Infinite) => _task.Flush(timeoutMilliseconds);
        public void LogVerbose(string message,
                [CallerFilePath] string filePath = "",
                [CallerLineNumber] int lineNumber = 0,
                [CallerMemberName] string memberName = "") => Log(ILogger.LogLevel.Verbose, message, filePath, lineNumber, memberName);
        public void LogInfo(string message,
                 [CallerFilePath] string filePath = "",
                 [CallerLineNumber] int lineNumber = 0,
                 [CallerMemberName] string memberName = "") => Log(ILogger.LogLevel.Info, message, filePath, lineNumber, memberName);
        public void LogWarning(string message,
                 [CallerFilePath] string filePath = "",
                 [CallerLineNumber] int lineNumber = 0,
                 [CallerMemberName] string memberName = "") => Log(ILogger.LogLevel.Warning, message, filePath, lineNumber, memberName);
        public void LogError(string message,
                 [CallerFilePath] string filePath = "",
                 [CallerLineNumber] int lineNumber = 0,
                 [CallerMemberName] string memberName = "") => Log(ILogger.LogLevel.Error, message, filePath, lineNumber, memberName);
        public void LogException(Exception ex,
                 [CallerFilePath] string filePath = "",
                 [CallerLineNumber] int lineNumber = 0,
                 [CallerMemberName] string memberName = "") => Log(ILogger.LogLevel.Error, ex.Message, filePath, lineNumber, memberName);
        private void Log(ILogger.LogLevel logLevel,
                 string message,
                 string filePath,
                 int lineNumber,
                 string memberName)
        {
            if (logLevel < _logLevel || _stopping) return;
            if (_task.GetNumberOfItemsQueued() > _task.Configuration.MaxUnflushedLogStatements) return;
            if (_task.GetNumberOfItemsQueued() == _task.Configuration.MaxUnflushedLogStatements)
            {
                _task.Queue("Dropped Log Statements Because Number of Queued Statements Exceeds MaxUnflushedLogStatements");
                Flush();
                return;
            }
            string logStatment = $"{logLevel.ToString().ToUpper()}; {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}; [{memberName}] {message}; (File: {filePath}, Line: {lineNumber}, Thread: {Environment.CurrentManagedThreadId})";
            _task.Queue(logStatment, _task.Configuration.FlushHighWaterMark);
            if (_task.GetNumberOfItemsQueued() >= _task.Configuration.FlushHighWaterMark) Thread.Yield();
        }
    }
}
