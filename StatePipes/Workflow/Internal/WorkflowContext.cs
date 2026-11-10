using StatePipes.Common.Internal;
using StatePipes.StateMachine.Internal;

namespace StatePipes.Workflow.Internal
{
    /// <summary>The workflow instance being built or fired on this thread; read by workflow state constructors.</summary>
    internal sealed record WorkflowContext(BaseStateMachine Machine, string CorrelationId, ITimerService Timers)
    {
        [ThreadStatic] public static WorkflowContext? Current;
    }
}
