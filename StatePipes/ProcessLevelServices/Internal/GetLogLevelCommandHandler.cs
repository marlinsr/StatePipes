using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.ProcessLevelServices.Internal
{
    internal class GetLogLevelCommandHandler(IStatePipesService eventBus) : IMessageHandler<GetLogLevelCommand>
    {
        private readonly IStatePipesService _eventBus = eventBus;
        public void HandleMessage(GetLogLevelCommand message, BusConfig? responseInfo, bool isResponse)
        {
            if (LoggerHolder.Log == null) return;
            _eventBus.PublishEvent(new LogLevelEvent(LoggerHolder.Log.GetLogLevel()));
        }
    }
}
