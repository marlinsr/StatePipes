using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.StateMachine;

namespace StatePipes.Test.Workflow.Samples.Cart
{
    public interface CartWorkflow : IWorkflow { }

    public record CartLine(string Sku, int Qty);

    public class AddItem : BaseWorkflowTriggerCommand<CartWorkflow> { public string Sku { get; init; } = ""; public int Qty { get; init; } }
    public class Checkout : BaseWorkflowTriggerCommand<CartWorkflow> { }
    public class PriceQuoted : BaseWorkflowTriggerCommand<CartWorkflow> { public decimal Total { get; init; } }
    public class CartAbandoned : BaseWorkflowTriggerCommand<CartWorkflow> { }

    public class CartCheckedOut : IEvent { public string CorrelationId { get; init; } = ""; }

    internal class CartOpenState : BaseWorkflowState<CartWorkflow>, IFirstStateForStateMachine
    {
        private List<CartLine> _lines = [];
        private IDelayedMessageSender<CartAbandoned> _abandon;

        public CartOpenState() => _abandon = CreateDelayedMessageSender<CartAbandoned>();

        [PermitReentryIf]
        private bool OnAdd(AddItem cmd, BusConfig? r)
        {
            _lines.Add(new(cmd.Sku, cmd.Qty));
            _abandon.StartOneShot(TimeSpan.FromDays(7), new CartAbandoned { CorrelationId = CorrelationId });
            return false;
        }

        [PermitIf(typeof(PricingState))]
        private bool OnCheckout(Checkout cmd, BusConfig? r) => _lines.Count > 0;

        [PermitIf(typeof(AbandonedState))]
        private bool OnAbandoned(CartAbandoned cmd, BusConfig? r) => true;
    }

    internal class PricingState : BaseWorkflowState<CartWorkflow>
    {
        public override void OnEntry() => SendCommand(new QuotePrice { CorrelationId = CorrelationId });

        [PermitIf(typeof(CheckedOutState))]
        private bool OnQuoted(PriceQuoted cmd, BusConfig? r) => true;

        [IgnoreIf]
        private bool OnAbandoned(CartAbandoned cmd, BusConfig? r) => true;
    }

    internal class CheckedOutState : BaseWorkflowState<CartWorkflow>, IFinalWorkflowState
    {
        public override void OnEntry() => PublishEvent(new CartCheckedOut { CorrelationId = CorrelationId });
    }

    internal class AbandonedState : BaseWorkflowState<CartWorkflow>, IFinalWorkflowState { }

    // Singleton machine a cart asks for a price (request/reply by command, spec §3.4 rule 6).
    public interface PricingMachine : IStateMachine { }
    public class QuotePrice : BaseTriggerCommand<PricingMachine> { public string CorrelationId { get; init; } = ""; }

    internal class PricingReadyState : BaseStateMachineState<PricingMachine>, IFirstStateForStateMachine
    {
        [PermitReentryIf]
        private bool OnQuote(QuotePrice cmd, BusConfig? r)
        {
            SendCommand(new PriceQuoted { CorrelationId = cmd.CorrelationId, Total = 42m });
            return false;
        }
    }
}
