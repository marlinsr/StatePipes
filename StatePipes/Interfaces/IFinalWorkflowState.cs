namespace StatePipes.Interfaces
{
    /// <summary>Marks a terminal workflow state; entering it deletes the workflow instance and its timers.</summary>
    public interface IFinalWorkflowState
    {
    }
}
