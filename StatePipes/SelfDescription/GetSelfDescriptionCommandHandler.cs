using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.SelfDescription
{
    internal class GetSelfDescriptionCommandHandler(TypeSerializationList typeSerializations, IStatePipesService eventBus) : IMessageHandler<GetSelfDescriptionCommand>
    {
        private readonly TypeSerializationList _typeSerializations = typeSerializations;
        private readonly IStatePipesService _eventBus = eventBus;
        public void HandleMessage(GetSelfDescriptionCommand message, BusConfig? responseInfo, bool isResponse)
        {
            if (responseInfo == null) return;
            _eventBus.SendResponse(new SelfDescriptionEvent(_typeSerializations), responseInfo);
        }
    }
}
