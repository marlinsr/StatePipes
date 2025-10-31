using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Internal;

namespace StatePipes.StateMachine
{
    public abstract class BaseStateMachineState<StateMachineType> : IStateMachineState where StateMachineType : IStateMachine
    {
        private BaseStateMachine? _stateMachine { get; set; }
        public BaseStateMachineState()
        {
            if (TemporaryStateMachineHolder.BaseStateMachine == null) throw new Exception("TemporaryStateMachineHolder.BaseStateMachine == null");
            _stateMachine = TemporaryStateMachineHolder.BaseStateMachine;
        }
        protected bool Fire<TTrigger>(TTrigger trigger, BusConfig? responseInfo = null) where TTrigger : ITrigger => _stateMachine?.Fire(trigger, responseInfo) ?? false;
        protected bool FireExternal<TStateMachine, BaseTriggerCommandType>(BaseTriggerCommandType trigger, BusConfig? responseInfo = null) 
            where TStateMachine : IStateMachine where BaseTriggerCommandType : BaseTriggerCommand<TStateMachine>
        {
            return _stateMachine?.FireExternal<TStateMachine, BaseTriggerCommandType>(trigger, responseInfo) ?? false;
        }
        protected void SendCurrentStatusAllStateMachines() => _stateMachine?.SendCurrentStatusAllStateMachines();
        protected string GetCurrentStateForExternal<TStateMachine>() where TStateMachine : IStateMachine => _stateMachine?.CurrentState ?? string.Empty;
        protected void SendCommand<TCommand>(TCommand trigger, BusConfig? responseInfo = null) where TCommand : class, ICommand => _stateMachine?.SendCommand(trigger, responseInfo);
        protected void PublishEvent<TEvent>(TEvent ev) where TEvent : class, IEvent => _stateMachine?.PublishEvent(ev, this.GetType().Name);
        protected void SendResponse<TEvent>(TEvent ev, BusConfig responseInfo) where TEvent : class, IEvent => _stateMachine?.SendResponse(ev, responseInfo, this.GetType().Name);
        protected void SendCurrentStatus() => _stateMachine?.SendCurrentStatus();
        protected IDelayedMessageSender<TMessage>? CreateDelayedMessageSender<TMessage>() where TMessage : class, IMessage => _stateMachine?.CreateDelayedMessageSender<TMessage>();
        protected TTrigger? GetCurrentTrigger<TTrigger>() where TTrigger : ITrigger
        {
            if (_stateMachine == null) return default;
            return _stateMachine.GetCurrentTrigger<TTrigger>();
        }
        protected BusConfig? GetCurrentResponseInfo()
        {
            if (_stateMachine == null) return null;
            return _stateMachine.GetCurrentResponseInfo();
        }
        public abstract void Configure(StateConfigurationWrapper stateConfig);
    }
}

