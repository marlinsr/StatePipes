using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.StateMachine;
using System.Collections.Concurrent;

namespace StatePipes.Test.Workflow.Samples.Counting
{
    public interface CountingWorkflow : IWorkflow { }
    public class CountGo : BaseWorkflowTriggerCommand<CountingWorkflow> { }
    public class CountNext : BaseWorkflowTriggerCommand<CountingWorkflow> { }
    public class CountReset : BaseWorkflowTriggerCommand<CountingWorkflow> { }
    public class CountFinish : BaseWorkflowTriggerCommand<CountingWorkflow> { }
    public class CountBoom : BaseWorkflowTriggerCommand<CountingWorkflow> { }

    public static class Entries
    {
        public static readonly ConcurrentDictionary<string, int> Count = new();
        public static void Hit(string state) => Count.AddOrUpdate(state, 1, (_, n) => n + 1);
        public static int Of(string state) => Count.GetValueOrDefault(state);
    }

    internal class CountFirstState : BaseWorkflowState<CountingWorkflow>, IFirstStateForStateMachine
    {
        public override void OnEntry() => Entries.Hit(nameof(CountFirstState));
        [PermitIf(typeof(CountParentState))]
        private bool OnGo(CountGo cmd, BusConfig? r) => true;
        [PermitIf(typeof(CountDoneState))]
        private bool OnBoom(CountBoom cmd, BusConfig? r) => throw new InvalidOperationException("boom");
    }

    internal class CountParentState : BaseWorkflowState<CountingWorkflow>
    {
        public override void OnEntry() => Entries.Hit(nameof(CountParentState));
        [PermitReentryIf]
        private bool OnReset(CountReset cmd, BusConfig? r) => true;
        [PermitIf(typeof(CountDoneState))]
        private bool OnFinish(CountFinish cmd, BusConfig? r) => true;
    }

    internal class CountChildAState : ParentedBaseWorkflowState<CountingWorkflow, CountParentState>, IFirstSubstate
    {
        public override void OnEntry() => Entries.Hit(nameof(CountChildAState));
        [PermitIf(typeof(CountChildBState))]
        private bool OnNext(CountNext cmd, BusConfig? r) => true;
    }

    internal class CountChildBState : ParentedBaseWorkflowState<CountingWorkflow, CountParentState>
    {
        public override void OnEntry() => Entries.Hit(nameof(CountChildBState));
    }

    internal class CountDoneState : BaseWorkflowState<CountingWorkflow>, IFinalWorkflowState { }
}
