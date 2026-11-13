using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    /// <summary>Reply to <see cref="GetWorkflowDiagramsCommand"/>; echoes the requested CorrelationId.</summary>
    public class WorkflowDiagramsEvent(string correlationId, IReadOnlyList<WorkflowInstanceDiagram> workflows) : IEvent
    {
        public string CorrelationId { get; } = correlationId;
        public IReadOnlyList<WorkflowInstanceDiagram> Workflows { get; } = workflows;
    }
}
