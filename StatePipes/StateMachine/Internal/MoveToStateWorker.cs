namespace StatePipes.StateMachine.Internal
{
    internal class MoveToStateWorker
    {
        private readonly StateWorker _state;
        private readonly BaseStateMachine _stateMachine;
        private readonly Type _destinationState;
        public MoveToStateWorker(StateWorker state, BaseStateMachine stateMachine, Type destinationState)
        {
            _state = state;
            _stateMachine = stateMachine;
            _state.SetMoveToStateHelper(this);
            _destinationState = destinationState;
        }
        public StateConfigurationWrapper Configure(StateConfigurationWrapper stateConfig) => stateConfig.Permit( typeof(MoveToState), _destinationState);
        public void OnEntry()
        {
            if(!_state.DisableAutomaticMoveToState) _stateMachine.Fire(new MoveToState());
        }
    }
}
