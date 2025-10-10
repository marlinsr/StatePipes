using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.ProcessLevelServices.Internal
{
    internal class SetLogLevelCommandHandler(IStatePipesService eventBus) : IMessageHandler<SetLogLevelCommand>
    {
        private readonly IStatePipesService _eventBus = eventBus;
        public void HandleMessage(SetLogLevelCommand message, BusConfig? responseInfo, bool isResponse)
        {
            if(LoggerHolder.Log == null) return;
            LoggerHolder.Log.SetLogLevel(message.LogLevel);
            _eventBus.PublishEvent(new LogLevelEvent(LoggerHolder.Log.GetLogLevel()));
        }
    }
}
