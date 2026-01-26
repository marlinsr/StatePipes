using StatePipes.Interfaces;

namespace StatePipes.Comms.Internal
{
    internal interface IStatePipesProxyInternal : IStatePipesProxy
    {
        void Subscribe<TEvent>(string? receivedEventTypeFullName, Action<TEvent, BusConfig, bool> handler) where TEvent : class;
        void SendCommand<TCommand>(string? sendCommandTypeFullName, TCommand command) where TCommand : class;
    }
}
