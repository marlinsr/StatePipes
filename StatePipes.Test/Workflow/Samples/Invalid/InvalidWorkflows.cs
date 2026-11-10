using StatePipes.Interfaces;
using StatePipes.StateMachine;

// One workflow per startup-validation rule (spec §3.4). Each is otherwise valid so only one rule fails.
namespace StatePipes.Test.Workflow.Samples.Invalid.CtorParams
{
    public interface CtorParamsWorkflow : IWorkflow { }
    internal class CtorParamsFirstState : BaseWorkflowState<CtorParamsWorkflow>, IFirstStateForStateMachine { public CtorParamsFirstState(int x) { } }
    internal class CtorParamsDoneState : BaseWorkflowState<CtorParamsWorkflow>, IFinalWorkflowState { }
}
namespace StatePipes.Test.Workflow.Samples.Invalid.NoFirst
{
    public interface NoFirstWorkflow : IWorkflow { }
    internal class NoFirstDoneState : BaseWorkflowState<NoFirstWorkflow>, IFinalWorkflowState { }
}
namespace StatePipes.Test.Workflow.Samples.Invalid.TwoFirst
{
    public interface TwoFirstWorkflow : IWorkflow { }
    internal class TwoFirstAState : BaseWorkflowState<TwoFirstWorkflow>, IFirstStateForStateMachine { }
    internal class TwoFirstBState : BaseWorkflowState<TwoFirstWorkflow>, IFirstStateForStateMachine { }
    internal class TwoFirstDoneState : BaseWorkflowState<TwoFirstWorkflow>, IFinalWorkflowState { }
}
namespace StatePipes.Test.Workflow.Samples.Invalid.NoFinal
{
    public interface NoFinalWorkflow : IWorkflow { }
    internal class NoFinalFirstState : BaseWorkflowState<NoFinalWorkflow>, IFirstStateForStateMachine { }
}
namespace StatePipes.Test.Workflow.Samples.Invalid.SingletonTrigger
{
    public interface SingletonTriggerWorkflow : IWorkflow { }
    internal class SingletonTriggerFirstState : BaseWorkflowState<SingletonTriggerWorkflow>, IFirstStateForStateMachine { }
    internal class SingletonTriggerDoneState : BaseWorkflowState<SingletonTriggerWorkflow>, IFinalWorkflowState { }
    public class SingletonStyleTrigger : BaseTriggerCommand<SingletonTriggerWorkflow> { }
}
namespace StatePipes.Test.Workflow.Samples.Invalid.SingletonState
{
    public interface SingletonStateWorkflow : IWorkflow { }
    internal class SingletonStateFirstState : BaseWorkflowState<SingletonStateWorkflow>, IFirstStateForStateMachine { }
    internal class SingletonStateDoneState : BaseWorkflowState<SingletonStateWorkflow>, IFinalWorkflowState { }
    internal class SingletonStyleState : BaseStateMachineState<SingletonStateWorkflow> { }
}
namespace StatePipes.Test.Workflow.Samples.Invalid.Duplicate
{
    public interface DuplicateWorkflow : IWorkflow { }
    internal class DuplicateFirstState : BaseWorkflowState<DuplicateWorkflow>, IFirstStateForStateMachine { }
    internal class DupState : BaseWorkflowState<DuplicateWorkflow>, IFinalWorkflowState { }
}
namespace StatePipes.Test.Workflow.Samples.Invalid.Duplicate.Other
{
    internal class DupState : BaseWorkflowState<DuplicateWorkflow> { }
}
namespace StatePipes.Test.Workflow.Samples.Invalid.BadTrigger
{
    public interface BadTriggerWorkflow : IWorkflow { }
    internal class BadTriggerFirstState : BaseWorkflowState<BadTriggerWorkflow>, IFirstStateForStateMachine { }
    internal class BadTriggerDoneState : BaseWorkflowState<BadTriggerWorkflow>, IFinalWorkflowState { }
    public class UnserializableTrigger : BaseWorkflowTriggerCommand<BadTriggerWorkflow> { public Func<int> Callback { get; init; } = () => 1; }
}
