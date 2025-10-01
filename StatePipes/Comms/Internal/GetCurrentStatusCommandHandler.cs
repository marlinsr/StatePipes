using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.Comms.Internal
{
    internal class GetCurrentStatusCommandHandler(IStatePipesService bus, IGetCurrentStatusCommand trigger) : IMessageHandler<GetCurrentStatusCommand>
    {
        public void HandleMessage(GetCurrentStatusCommand message, BusConfig? responseInfo, bool isResponse) => bus.SendCommand(trigger, responseInfo);
    }
}
