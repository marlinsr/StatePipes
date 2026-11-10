using StatePipes.Interfaces;
using StatePipes.StateMachine;

namespace StatePipes.Test.Workflow.Samples.Doc
{
    public interface DocWorkflow : IWorkflow { }
    public class DocPing : BaseWorkflowTriggerCommand<DocWorkflow> { }

    internal class DocFirstState : BaseWorkflowState<DocWorkflow>, IFirstStateForStateMachine
    {
        private List<int> _numbers = [];
        public string Name { get; set; } = "initial";
        private readonly int _fixed = 1;
        private object _payload = 5;
        private IDelayedMessageSender<DocPing> _timer;
        public DocFirstState() => _timer = CreateDelayedMessageSender<DocPing>();

        public List<int> Numbers => _numbers;
        public int Fixed => _fixed;
        public object Payload { get => _payload; set => _payload = value; }
        public IDelayedMessageSender<DocPing> Timer => _timer;
    }

    internal class DocDoneState : BaseWorkflowState<DocWorkflow>, IFinalWorkflowState { }

    public record DocPayload(string A);
}
