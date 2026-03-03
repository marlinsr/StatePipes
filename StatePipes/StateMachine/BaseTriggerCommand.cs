using StatePipes.Interfaces;

namespace StatePipes.StateMachine
{
    public class BaseTriggerCommand<StateMachineType> : ITrigger where StateMachineType : IStateMachine
    {
    }
}
