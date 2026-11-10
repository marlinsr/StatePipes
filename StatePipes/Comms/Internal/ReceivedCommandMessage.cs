namespace StatePipes.Comms.Internal
{
    internal class ReceivedCommandMessage(object command, BusConfig replyTo, int retryCount = 0, bool isTimerDelivery = false)
    {
        public object Command { get; } = command;
        public BusConfig ReplyTo { get; } = replyTo;
        public int RetryCount { get; } = retryCount;
        public bool IsTimerDelivery { get; } = isTimerDelivery;
    }
}
