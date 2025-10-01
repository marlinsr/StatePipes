using StatePipes.StateMachine;

namespace StatePipes.Interfaces
{
    public interface IStateMachineState
    {
        void Configure(StateConfigurationWrapper stateConfig);
    }
}
