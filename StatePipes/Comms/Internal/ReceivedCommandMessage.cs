using StatePipes.Interfaces;

namespace StatePipes.Comms.Internal
{
    internal class ReceivedCommandMessage(ICommand command, BusConfig replyTo)
    {
        public ICommand Command { get; } = command;
        public BusConfig ReplyTo { get; } = replyTo;
    }
}
