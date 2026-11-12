using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    /// <summary>Asks a service for the diagram of every running workflow instance with this CorrelationId.</summary>
    public class GetWorkflowDiagramsCommand(string correlationId) : ICommand
    {
        public string CorrelationId { get; } = correlationId;
    }
}
