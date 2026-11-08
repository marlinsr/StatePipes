namespace StatePipes.Comms.Internal
{
    /// <summary>Per-dispatch facts about the command currently being handled on the bus thread.</summary>
    internal static class CommandDispatchContext
    {
        [ThreadStatic] public static int RetryCount;
        [ThreadStatic] public static bool IsTimerDelivery;

        public static void Run(ReceivedCommandMessage message, Action dispatch)
        {
            RetryCount = message.RetryCount;
            IsTimerDelivery = message.IsTimerDelivery;
            try { dispatch(); }
            finally
            {
                RetryCount = 0;
                IsTimerDelivery = false;
            }
        }
    }

    /// <summary>Puts a command back on the end of this service's own command queue.</summary>
    internal interface ICommandRequeuer
    {
        void Requeue(object command, BusConfig replyTo, int retryCount, bool isTimerDelivery);
    }
}
