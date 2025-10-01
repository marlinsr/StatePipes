using StatePipes.Interfaces;
using StatePipes.ProcessLevelServices.Internal;

namespace StatePipes.ProcessLevelServices
{
    public static class LoggerHolder
    {
        public static ILogger? Log { get; private set; }
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
