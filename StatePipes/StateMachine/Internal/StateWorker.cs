using StatePipes.Interfaces;

namespace StatePipes.StateMachine.Internal
{
    internal class StateWorker(BaseStateMachine _stateMachine, IStateMachineState _state, bool _disableAutomaticMoveToState)
    {
        private Action? _entryAction;
        private Action? _exitAction;
        private MoveToStateWorker? _moveToState;
        public bool DisableAutomaticMoveToState { get => _disableAutomaticMoveToState; }
        public void SetMoveToStateHelper(MoveToStateWorker moveToState) => _moveToState = moveToState;
        public void SetEntryAction(Action entryAction) => _entryAction = entryAction;
        public void SetExitAction(Action exitAction) => _exitAction = exitAction;
#pragma warning disable IDE1006 // Naming Styles
        public void _OnEntry()
#pragma warning restore IDE1006 // Naming Styles
        {
            _entryAction?.Invoke();
            _moveToState?.OnEntry();
        }
#pragma warning disable IDE1006 // Naming Styles
        public void _OnExit() => _exitAction?.Invoke();
#pragma warning restore IDE1006 // Naming Styles
        public void Configure() => _state.Configure(new StateConfigurationWrapper(this, _stateMachine, _stateMachine.Configure(_state.GetType().Name).OnEntry(_OnEntry).OnExit(_OnExit)));
    }
}
