using StatePipes.Comms;
using StatePipes.Interfaces;

namespace StatePipes.StateMachine.Internal
{
    internal class BaseTriggerCommandHandler<T,S>(StateMachineManager stateMachineManager) 
        : IMessageHandler<T> where S : IStateMachine where T : BaseTriggerCommand<S> 
    {
        private readonly BaseStateMachine _stateMachine = stateMachineManager.GetStateMachine<S>();
        public void HandleMessage(T command, BusConfig? responseInfo, bool isResponse)
        {
            if (typeof(IInitTrigger).IsAssignableFrom(command.GetType()))
            {
                _stateMachine.Fire(new InitTrigger(), responseInfo);
            }
            else
            {
                _stateMachine.Fire(command, responseInfo);
            }
        }
    }
}
