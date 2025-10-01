using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.Comms.Internal
{
    internal class HeartbeatCommandHandler(IStatePipesService bus) : IMessageHandler<HeartbeatCommand>
    {
        private long _hertbeatCount = 0;
        public void HandleMessage(HeartbeatCommand command, BusConfig? responseInfo, bool isResponse)
        {
            unchecked
            {
                bus.PublishEvent(new HeartbeatEvent(_hertbeatCount++));
            }
        }
    }
}
