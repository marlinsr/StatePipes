using StatePipes.Interfaces;
using StatePipes.StateMachine;
using StatePipes.StateMachine.Internal;
using StatePipes.Test.Workflow.Samples.Cart;
using System.Reflection;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowTypesTest
    {
        [TestMethod]
        public void WorkflowState_WithoutContext_Throws()
        {
            var ex = Assert.ThrowsExactly<TargetInvocationException>(() => Activator.CreateInstance(typeof(CartOpenState), nonPublic: true));
            Assert.IsInstanceOfType<InvalidOperationException>(ex.InnerException);
            StringAssert.Contains(ex.InnerException!.Message, "Workflow states can only be created by the workflow engine");
        }

        [TestMethod]
        public void WorkflowState_ExposesNoFireExternal()
        {
            var names = typeof(BaseWorkflowState<>).GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m.Name).ToHashSet();
            foreach (var forbidden in new[] { "Fire", "FireExternal", "GetCurrentStateForExternal", "SendCurrentStatus", "SendCurrentStatusAllStateMachines" })
                Assert.IsFalse(names.Contains(forbidden), forbidden);
        }

        [TestMethod]
        public void AllStateMachineContainerSetup_SkipsWorkflows()
        {
            var setup = new AllStateMachineContainerSetup(typeof(WorkflowTypesTest).Assembly, false);
            Assert.IsFalse(setup.StateMachineTypes.Any(t => typeof(IWorkflow).IsAssignableFrom(t)));
            CollectionAssert.Contains(setup.StateMachineTypes.ToList(), typeof(PricingMachine));
        }

        [TestMethod]
        public void WorkflowTrigger_CarriesCorrelationId()
        {
            Assert.AreEqual("", new AddItem().CorrelationId);
            Assert.AreEqual("cart-1", new AddItem { CorrelationId = "cart-1" }.CorrelationId);
        }
    }
}
