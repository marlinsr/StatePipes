using StatePipes.StateMachine.Test;
using StatePipes.Test.Workflow.Samples.Cart;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class CartWorkflowTest
    {
        [TestMethod]
        public void AddItem_CreatesCartInOpenState()
        {
            using var wf = new WorkflowForTest<CartWorkflow>();
            wf.SendCommand(new AddItem { Sku = "sku-1", Qty = 2, CorrelationId = "cart-1" });
            Assert.AreEqual(nameof(CartOpenState), wf.CurrentState("cart-1"));
            Assert.IsTrue(wf.Exists("cart-1"));
            Assert.IsFalse(wf.Exists("cart-2"));
        }

        [TestMethod]
        public void AbandonTimer_AfterSevenDays_FinishesAndDeletes()
        {
            using var wf = new WorkflowForTest<CartWorkflow>();
            wf.SendCommand(new AddItem { Sku = "sku-1", CorrelationId = "cart-1" });
            wf.AdvanceTime(TimeSpan.FromDays(7));
            Assert.IsFalse(wf.Exists("cart-1"));
            Assert.AreEqual(0, wf.EventInvocations.Count);
        }

        [TestMethod]
        public void TimerSurvivesRehydration()
        {
            using var wf = new WorkflowForTest<CartWorkflow>();
            wf.SendCommand(new AddItem { Sku = "a", CorrelationId = "cart-1" });
            wf.AdvanceTime(TimeSpan.FromDays(3));
            wf.SendCommand(new AddItem { Sku = "b", CorrelationId = "cart-1" }); // restarts the persisted timer: due day 10
            wf.AdvanceTime(TimeSpan.FromDays(6));
            Assert.IsTrue(wf.Exists("cart-1"));
            wf.AdvanceTime(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1));
            Assert.IsFalse(wf.Exists("cart-1"));
        }

        [TestMethod]
        public void Checkout_RequestsPrice_ReplyFinishes()
        {
            using var wf = new WorkflowForTest<CartWorkflow>();
            wf.SendCommand(new AddItem { Sku = "a", CorrelationId = "cart-1" });
            wf.SendCommand(new Checkout { CorrelationId = "cart-1" });
            Assert.AreEqual(nameof(PricingState), wf.CurrentState("cart-1"));
            Assert.IsTrue(wf.CommandInvocations.OfType<QuotePrice>().Any(q => q.CorrelationId == "cart-1"));
            wf.SendCommand(new PriceQuoted { Total = 42m, CorrelationId = "cart-1" });
            Assert.IsTrue(wf.EventInvocations.OfType<CartCheckedOut>().Any(e => e.CorrelationId == "cart-1"));
            Assert.IsFalse(wf.Exists("cart-1"));
        }

        [TestMethod]
        public void GetField_ReadsPersistedLines()
        {
            using var wf = new WorkflowForTest<CartWorkflow>();
            wf.SendCommand(new AddItem { Sku = "a", Qty = 1, CorrelationId = "cart-1" });
            wf.SendCommand(new AddItem { Sku = "b", Qty = 2, CorrelationId = "cart-1" });
            var lines = wf.GetField<CartOpenState, List<CartLine>>("cart-1", "_lines");
            CollectionAssert.AreEqual(new[] { new CartLine("a", 1), new CartLine("b", 2) }, lines);
        }

        [TestMethod]
        public void SimulateLocked_DiscardsAfterMaxRetries()
        {
            using var wf = new WorkflowForTest<CartWorkflow>(lockedMaxRetries: 2);
            wf.SimulateLocked("cart-1");
            wf.SendCommand(new AddItem { Sku = "a", CorrelationId = "cart-1" });
            wf.AdvanceTime(TimeSpan.FromMilliseconds(200));
            wf.ReleaseLock("cart-1");
            wf.AdvanceTime(TimeSpan.FromSeconds(1));
            Assert.IsFalse(wf.Exists("cart-1"));
        }

        [TestMethod]
        public void SimulateLocked_ReleasedBeforeMaxRetries_Succeeds()
        {
            using var wf = new WorkflowForTest<CartWorkflow>(lockedMaxRetries: 10);
            wf.SimulateLocked("cart-1");
            wf.SendCommand(new AddItem { Sku = "a", CorrelationId = "cart-1" });
            wf.AdvanceTime(TimeSpan.FromMilliseconds(120));
            wf.ReleaseLock("cart-1");
            wf.AdvanceTime(TimeSpan.FromMilliseconds(100));
            Assert.IsTrue(wf.Exists("cart-1"));
        }
    }
}
