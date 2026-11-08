using StatePipes.Interfaces;

namespace StatePipes.StateMachine
{
    /// <summary>Base for every trigger of a workflow; <see cref="CorrelationId"/> selects the workflow instance.</summary>
    public class BaseWorkflowTriggerCommand<TWorkflow> : ITrigger where TWorkflow : IWorkflow
    {
        public string CorrelationId { get; init; } = string.Empty;
    }
}
