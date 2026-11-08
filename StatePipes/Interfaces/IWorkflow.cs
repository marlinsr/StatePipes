namespace StatePipes.Interfaces
{
    /// <summary>Marker for a workflow: a state machine with many persisted instances, each keyed by a CorrelationId.</summary>
    public interface IWorkflow : IStateMachine
    {
    }
}
