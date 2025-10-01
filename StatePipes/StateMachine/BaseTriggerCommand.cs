using StatePipes.Interfaces;

namespace StatePipes.StateMachine
{
    public class BaseTriggerCommand<StateMachineType> : ITrigger where StateMachineType : IStateMachine
    {
    }

    //SRM change Trigger definition
    //public interface ITrigger2<StateMachineType> : IMessage where StateMachineType : IStateMachine
    //{

    //}
}
