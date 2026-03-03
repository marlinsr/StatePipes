using Stateless;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Internal;
namespace StatePipes.StateMachine
{
    public class StateConfigurationWrapper
    {
        private readonly StateWorker _state;
        private readonly BaseStateMachine _stateMachine;
        private readonly StateMachine<string, string>.StateConfiguration _stateConfiguration;
        internal StateConfigurationWrapper(StateWorker state, BaseStateMachine stateMachine, StateMachine<string, string>.StateConfiguration stateConfiguration)
        {
            _state = state;
            _stateConfiguration = stateConfiguration;
            _stateMachine = stateMachine;
        }
        internal StateConfigurationWrapper(StateConfigurationWrapper priorConfiguration, StateMachine<string, string>.StateConfiguration stateConfiguration)
        {
            _state = priorConfiguration._state;
            _stateConfiguration = stateConfiguration;
            _stateMachine = priorConfiguration._stateMachine;
        }
        public StateConfigurationWrapper Permit<Trigger, DestinationState>() where Trigger : ITrigger where DestinationState : IStateMachineState
        {
            return Permit(typeof(Trigger), typeof(DestinationState));
        }
        public StateConfigurationWrapper Permit(Type TriggerType, Type DestinationStateType)
        {
            var result = _stateConfiguration.Permit(TriggerType.Name, DestinationStateType.Name);
            return new StateConfigurationWrapper(this,result);
        }
        public StateConfigurationWrapper PermitReentry<Trigger>() where Trigger : ITrigger => PermitReentry(typeof(Trigger));
        public StateConfigurationWrapper PermitReentry(Type TriggerType)
        {
            var result = _stateConfiguration.PermitReentry(TriggerType.Name);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper PermitReentryIf<Trigger>(Func<bool> guard, string? guardDescription = null) where Trigger : ITrigger => PermitReentryIf(typeof(Trigger), guard, guardDescription);
        internal StateConfigurationWrapper PermitReentryIf(Type triggerType, Func<bool> guard, string? guardDescription = null)
        {
            var result = _stateConfiguration.PermitReentryIf(triggerType.Name, guard, guardDescription);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper PermitReentryIf<Trigger>(params Tuple<Func<bool>, string>[] guards) where Trigger : ITrigger
        {
            var result = _stateConfiguration.PermitReentryIf(typeof(Trigger).Name, guards);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper PermitIf<Trigger, DestinationState>(Func<bool> guard, string? guardDescription = null) where Trigger : ITrigger where DestinationState : IStateMachineState
        => PermitIf(typeof(Trigger), typeof(DestinationState), guard, guardDescription);
        internal StateConfigurationWrapper PermitIf(Type triggerType, Type destinationState, Func<bool> guard, string? guardDescription = null) 
        {
            var result = _stateConfiguration.PermitIf(triggerType.Name, destinationState.Name, guard, guardDescription);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper PermitIf<Trigger, DestinationState>(params Tuple<Func<bool>, string>[] guards) where Trigger : ITrigger where DestinationState : IStateMachineState
        {
            var result = _stateConfiguration.PermitIf(typeof(Trigger).Name, typeof(DestinationState).Name, guards);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper Ignore<Trigger>() where Trigger : ITrigger
        {
            var result = _stateConfiguration.Ignore(typeof(Trigger).Name);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper IgnoreIf<Trigger>(Func<bool> guard, string? guardDescription = null) where Trigger : ITrigger => IgnoreIf(typeof(Trigger), guard, guardDescription);
        internal StateConfigurationWrapper IgnoreIf(Type triggerType, Func<bool> guard, string? guardDescription = null)
        {
            var result = _stateConfiguration.IgnoreIf(triggerType.Name, guard, guardDescription);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper IgnoreIf<Trigger>(params Tuple<Func<bool>, string>[] guards) where Trigger : ITrigger
        {
            var result = _stateConfiguration.IgnoreIf(typeof(Trigger).Name, guards);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper OnActivate(Action activateAction, string? activateActionDescription = null)
        {
            var result = _stateConfiguration.OnActivate(activateAction, activateActionDescription);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper OnDeactivate(Action deactivateAction, string? deactivateActionDescription = null)
        {
            var result = _stateConfiguration.OnDeactivate(deactivateAction, deactivateActionDescription);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper OnEntry(Action entryAction)
        {
            _state.SetEntryAction(entryAction);
            return this;
        }
        public StateConfigurationWrapper OnEntryFrom<Trigger>(Action entryAction, string? entryActionDescription = null) where Trigger : ITrigger
        {
            var result = _stateConfiguration.OnEntryFrom(typeof(Trigger).Name, entryAction, entryActionDescription);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper OnExit(Action exitAction)
        {
            _state.SetExitAction(exitAction);
            return this;
        }
        public StateConfigurationWrapper SubstateOf<SuperState>() where SuperState : IStateMachineState
        {
            var result = _stateConfiguration.SubstateOf(typeof(SuperState).Name);
            return new StateConfigurationWrapper(this, result);
        }
        public StateConfigurationWrapper MoveToState<DestinationState>() where DestinationState : IStateMachineState => MoveToState(typeof(DestinationState));
        internal StateConfigurationWrapper MoveToState(Type? destinationState) 
        {
            if (destinationState == null) return this;
            var moveToState = new MoveToStateWorker(_state, _stateMachine, destinationState);
            return moveToState.Configure(this);
        }
        public StateConfigurationWrapper RegisterEvent<TEvent>() where TEvent : IEvent => RegisterEvent(typeof(TEvent));
        internal StateConfigurationWrapper RegisterEvent(Type ev)
        {
            _stateMachine.EventRegistrationManager.RegisterEvent(ev, _stateConfiguration.State);
            return this;
        }
        public StateConfigurationWrapper RegisterCommand<TCommand>() where TCommand : ICommand => RegisterCommand(typeof(TCommand));
        internal StateConfigurationWrapper RegisterCommand(Type command)
        {
            _stateMachine.CommandRegistrationManager.RegisterCommand(command, _stateConfiguration.State);
            return this;
        }
    }
}
