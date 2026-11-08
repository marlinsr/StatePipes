using StatePipes.Common.Internal;
using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Internal;
using StatePipes.Workflow.Internal;

namespace StatePipes.StateMachine
{
    /// <summary>
    /// Base for workflow states. Workflow states have parameterless constructors and every field they declare is persisted
    /// with the workflow instance. Singletons are reached only by sending commands.
    /// </summary>
    public class BaseWorkflowState<TWorkflow> : IStateMachineState where TWorkflow : IWorkflow
    {
        private readonly WorkflowContext _workflowContext;
        public BaseWorkflowState()
        {
            _workflowContext = WorkflowContext.Current ?? throw new InvalidOperationException("Workflow states can only be created by the workflow engine");
        }
        private BaseStateMachine Machine => _workflowContext.Machine;
        protected string CorrelationId => _workflowContext.CorrelationId;
        protected void SendCommand<TCommand>(TCommand command, BusConfig? responseInfo = null) where TCommand : class, ICommand => Machine.SendCommand(command, responseInfo);
        protected void PublishEvent<TEvent>(TEvent ev) where TEvent : class, IEvent => Machine.PublishEvent(ev);
        protected void SendResponse<TEvent>(TEvent ev, BusConfig responseInfo) where TEvent : class, IEvent => Machine.SendResponse(ev, responseInfo);
        protected IDelayedMessageSender<TMessage> CreateDelayedMessageSender<TMessage>() where TMessage : class, IMessage =>
            new DelayedMessageSender<TMessage>(_workflowContext.Timers, Guid.NewGuid());
        protected TTrigger? GetCurrentTrigger<TTrigger>() where TTrigger : ITrigger => (TTrigger?)GetCurrentTrigger(typeof(TTrigger));
        internal ITrigger? GetCurrentTrigger(Type triggerType) => Machine.GetCurrentTrigger(triggerType);
        protected BusConfig? GetCurrentResponseInfo() => Machine.GetCurrentResponseInfo();
        public virtual void Configure(StateConfigurationWrapper stateConfig)
        {
            StateHandlerScanner.Configure(this, stateConfig, GetCurrentTrigger, GetCurrentResponseInfo,
                typeof(ParentedBaseWorkflowState<,>), typeof(BaseWorkflowState<>));
            stateConfig.OnActivate(OnActivate).OnDeactivate(OnDeActivate).OnEntry(OnEntry).OnExit(OnExit);
        }
        public virtual void OnEntry() { }
        public virtual void OnExit() { }
        public virtual void OnActivate() { }
        public virtual void OnDeActivate() { }
    }
}
