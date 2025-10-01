using System.Runtime.CompilerServices;
namespace StatePipes.Interfaces
{
    public interface ILogger
    {
        public enum LogLevel { Verbose, Info, Warning, Error }
        void Start();
        void Stop();
        void SetLogLevel(LogLevel logLevel);
        void Flush(CancellationToken token, int timeoutMilliseconds = Timeout.Infinite);
        void Flush(int timeoutMilliseconds = Timeout.Infinite);
        void LogVerbose(string message,
                [CallerFilePath] string filePath = "",
                [CallerLineNumber] int lineNumber = 0,
                [CallerMemberName] string memberName = "");
        void LogInfo(string message,
                 [CallerFilePath] string filePath = "",
                 [CallerLineNumber] int lineNumber = 0,
                 [CallerMemberName] string memberName = "");
        void LogWarning(string message,
                 [CallerFilePath] string filePath = "",
                 [CallerLineNumber] int lineNumber = 0,
                 [CallerMemberName] string memberName = "");
        void LogError(string message,
                 [CallerFilePath] string filePath = "",
                 [CallerLineNumber] int lineNumber = 0,
                 [CallerMemberName] string memberName = "");
        void LogException(Exception ex,
                 [CallerFilePath] string filePath = "",
                 [CallerLineNumber] int lineNumber = 0,
                 [CallerMemberName] string memberName = "");
    }
}
