using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using StatePipes.Messages;
using StatePipes.StateMachine;

namespace StatePipes.Workflow.Internal
{
    /// <summary>Routes one workflow trigger type to its workflow's engine; one is registered per trigger type.</summary>
    internal class BaseWorkflowTriggerCommandHandler<TCommand, TWorkflow>(WorkflowEngineRegistry registry) : IMessageHandler<TCommand>
        where TWorkflow : IWorkflow where TCommand : BaseWorkflowTriggerCommand<TWorkflow>
    {
        public void HandleMessage(TCommand message, BusConfig? responseInfo, bool isResponse) =>
            registry.Get(typeof(TWorkflow)).Handle(message, message.CorrelationId, responseInfo,
                CommandDispatchContext.RetryCount, CommandDispatchContext.IsTimerDelivery);
    }

    /// <summary>The service's workflow engines, and the timer poller that feeds them; disposed with the container.</summary>
    internal sealed class WorkflowEngineRegistry : IDisposable
    {
        private readonly Dictionary<Type, WorkflowEngine> _engines = [];
        public WorkflowTimerPoller? Poller { get; set; }
        public IDisposable? OwnedStore { get; set; }
        public void Add(Type workflowType, WorkflowEngine engine) => _engines.Add(workflowType, engine);
        public WorkflowEngine Get(Type workflowType) => _engines[workflowType];
        /// <summary>Every running instance with this correlationId, one per workflow type at most, ordered by workflow type.</summary>
        public IReadOnlyList<WorkflowInstanceDiagram> Interrogate(string correlationId) =>
            [.. _engines.Values.Select(e => e.Interrogate(correlationId)).OfType<WorkflowInstanceDiagram>()
                .OrderBy(d => d.WorkflowType, StringComparer.Ordinal)];
        public void Dispose()
        {
            Poller?.Dispose();
            OwnedStore?.Dispose();
        }
    }
}
