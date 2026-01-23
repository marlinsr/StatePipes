using StatePipes.Interfaces;

namespace StatePipes.Comms.Internal
{
    internal class ReceivedCommandMessage(object command, BusConfig replyTo)
    {
        public object Command { get; } = command;
        public BusConfig ReplyTo { get; } = replyTo;
    }
}
