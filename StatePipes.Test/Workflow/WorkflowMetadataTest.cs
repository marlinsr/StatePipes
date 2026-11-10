using StatePipes.Test.Workflow.Samples.Cart;
using StatePipes.Test.Workflow.Samples.Invalid.BadTrigger;
using StatePipes.Test.Workflow.Samples.Invalid.CtorParams;
using StatePipes.Test.Workflow.Samples.Invalid.Duplicate;
using StatePipes.Test.Workflow.Samples.Invalid.NoFinal;
using StatePipes.Test.Workflow.Samples.Invalid.NoFirst;
using StatePipes.Test.Workflow.Samples.Invalid.SingletonState;
using StatePipes.Test.Workflow.Samples.Invalid.SingletonTrigger;
using StatePipes.Test.Workflow.Samples.Invalid.TwoFirst;
using StatePipes.Workflow.Internal;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowMetadataTest
    {
        private static void AssertInvalid(Type workflowType, string offendingName)
        {
            var ex = Assert.ThrowsExactly<WorkflowValidationException>(() => WorkflowMetadata.Build(workflowType));
            StringAssert.Contains(ex.Message, offendingName);
        }

        [TestMethod] public void ConstructorWithParameters_Fails() => AssertInvalid(typeof(CtorParamsWorkflow), "CtorParamsFirstState");
        [TestMethod] public void NoFirstState_Fails() => AssertInvalid(typeof(NoFirstWorkflow), "NoFirstWorkflow");
        [TestMethod] public void TwoFirstStates_Fails() => AssertInvalid(typeof(TwoFirstWorkflow), "TwoFirstWorkflow");
        [TestMethod] public void NoFinalState_Fails() => AssertInvalid(typeof(NoFinalWorkflow), "NoFinalWorkflow");
        [TestMethod] public void BaseTriggerCommandOnWorkflow_Fails() => AssertInvalid(typeof(SingletonTriggerWorkflow), "SingletonStyleTrigger");
        [TestMethod] public void BaseStateMachineStateOnWorkflow_Fails() => AssertInvalid(typeof(SingletonStateWorkflow), "SingletonStyleState");
        [TestMethod] public void DuplicateStateNames_Fails() => AssertInvalid(typeof(DuplicateWorkflow), "DupState");

        [TestMethod]

        public void TriggerNotRoundTrippable_Fails() => AssertInvalid(typeof(BadTriggerWorkflow), "UnserializableTrigger");

        [TestMethod]
        public void Cart_Metadata()
        {
            var md = WorkflowMetadata.Build(typeof(CartWorkflow));
            Assert.AreEqual(typeof(CartWorkflow).FullName, md.WorkflowTypeName);
            Assert.AreEqual(typeof(CartOpenState), md.FirstStateType);
            CollectionAssert.AreEquivalent(new[] { nameof(CheckedOutState), nameof(AbandonedState) }, md.FinalStateNames.ToList());
            CollectionAssert.AreEquivalent(new[] { "_lines", "_abandon" }, md.PersistedFields[nameof(CartOpenState)].Select(f => f.Name).ToList());
            Assert.AreEqual(0, md.PersistedFields[nameof(PricingState)].Count);
            CollectionAssert.AreEquivalent(new[] { typeof(AddItem), typeof(Checkout), typeof(PriceQuoted), typeof(CartAbandoned) }, md.TriggerTypes.ToList());
            Assert.AreEqual(4, md.StateTypes.Count);
        }

        [TestMethod]
        public void Cart_IsDeclared()
        {
            var md = WorkflowMetadata.Build(typeof(CartWorkflow));
            Assert.IsTrue(md.IsDeclared(nameof(CartOpenState), typeof(AddItem)));
            Assert.IsFalse(md.IsDeclared(nameof(PricingState), typeof(AddItem)));
            Assert.IsTrue(md.IsDeclared(nameof(PricingState), typeof(CartAbandoned)));
            Assert.IsNull(md.ParentOf(nameof(PricingState)));
        }
    }
}
