using Mono.Cecil;
using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Internal;

namespace StatePipes.StateMachine
{
    public class BaseStateMachineState<StateMachineType> : IStateMachineState where StateMachineType : IStateMachine
    {
#pragma warning disable IDE1006 // Naming Styles
        private BaseStateMachine? _stateMachine { get; set; }
#pragma warning restore IDE1006 // Naming Styles
        public BaseStateMachineState()
        {
            if (TemporaryStateMachineHolder.BaseStateMachine == null) throw new Exception("TemporaryStateMachineHolder.BaseStateMachine == null");
            _stateMachine = TemporaryStateMachineHolder.BaseStateMachine;
        }
        protected bool Fire<TTrigger>(TTrigger trigger, BusConfig? responseInfo = null) where TTrigger : ITrigger => _stateMachine?.Fire(trigger, responseInfo) ?? false;
        protected bool FireExternal<TStateMachine, BaseTriggerCommandType>(BaseTriggerCommandType trigger, BusConfig? responseInfo = null)
            where TStateMachine : IStateMachine where BaseTriggerCommandType : BaseTriggerCommand<TStateMachine> =>
            _stateMachine?.FireExternal<TStateMachine, BaseTriggerCommandType>(trigger, responseInfo) ?? false;
        protected void SendCurrentStatusAllStateMachines() => _stateMachine?.SendCurrentStatusAllStateMachines();
        protected string GetCurrentStateForExternal<TStateMachine>() where TStateMachine : IStateMachine => _stateMachine?.CurrentState ?? string.Empty;
        protected void SendCommand<TCommand>(TCommand trigger, BusConfig? responseInfo = null) where TCommand : class, ICommand => _stateMachine?.SendCommand(trigger, responseInfo);
        protected void PublishEvent<TEvent>(TEvent ev) where TEvent : class, IEvent => _stateMachine?.PublishEvent(ev);
        protected void SendResponse<TEvent>(TEvent ev, BusConfig responseInfo) where TEvent : class, IEvent => _stateMachine?.SendResponse(ev, responseInfo);
        protected void SendCurrentStatus() => _stateMachine?.SendCurrentStatus();
        protected IDelayedMessageSender<TMessage>? CreateDelayedMessageSender<TMessage>() where TMessage : class, IMessage => _stateMachine?.CreateDelayedMessageSender<TMessage>();
        protected TTrigger? GetCurrentTrigger<TTrigger>() where TTrigger : ITrigger => (TTrigger?)GetCurrentTrigger(typeof(TTrigger));
        internal ITrigger? GetCurrentTrigger(Type triggerType)
        {
            if (_stateMachine == null) return default;
            return _stateMachine.GetCurrentTrigger(triggerType);
        }
        protected BusConfig? GetCurrentResponseInfo()
        {
            if (_stateMachine == null) return null;
            return _stateMachine.GetCurrentResponseInfo();
        }
        public virtual void Configure(StateConfigurationWrapper stateConfig)
        {
            StateHandlerScanner.Configure(this, stateConfig, GetCurrentTrigger, GetCurrentResponseInfo,
                typeof(ParentedBaseStateMachineState<,>), typeof(BaseStateMachineState<>));
            stateConfig.OnActivate(OnActivate).OnDeactivate(OnDeActivate).OnEntry(OnEntry).OnExit(OnExit);
        }
        protected static IEnumerable<Type> GetEventTypesForState(Type thisType, string methodName) => StateHandlerScanner.GetEventTypes(thisType, methodName);
        protected static IEnumerable<Type> GetCommandTypesForState(Type stateType) => StateHandlerScanner.GetCommandTypes(stateType, typeof(BaseStateMachineState<>));
        public virtual void OnEntry() { }
        public virtual void OnExit() { }
        public virtual void OnActivate() { }
        public virtual void OnDeActivate() { }
    }
}

