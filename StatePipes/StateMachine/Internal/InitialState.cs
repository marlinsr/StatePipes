using StatePipes.Interfaces;

namespace StatePipes.StateMachine.Internal
{ 
    internal class InitialState : IStateMachineState
    {
        public void Configure(StateConfigurationWrapper config) { }
        internal void SetStateMachine(BaseStateMachine stateMachine) { }
    }
}
