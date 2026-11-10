using StatePipes.Interfaces;
using StatePipes.ProcessLevelServices.Internal;

namespace StatePipes.ProcessLevelServices
{
    public static class LoggerHolder
    {
        public static ILogger? Log { get; private set; }
        /// <summary>Replaces the process logger (tests only); returns the previous one so it can be restored.</summary>
        internal static ILogger? SetLoggerForTest(ILogger? logger)
        {
            var previous = Log;
            Log = logger;
            return previous;
        }
        public static void InitalizeLogger()
        {
            if (Log == null)
            {
                var logger = new Logger();
                logger.Start();
                Log = logger;
            }
        }
    }
}
