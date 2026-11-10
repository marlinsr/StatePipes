using StatePipes.StateMachine;
using StatePipes.StateMachine.Internal;
using StatePipes.Test.StateMachine.Samples;

namespace StatePipes.Test.StateMachine
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class StateHandlerScannerTest
    {
        [TestMethod]
        public void GetHandlers_ReportsKindTriggerDestination()
        {
            var handlers = StateHandlerScanner.GetHandlers(typeof(PumpIdleState))
                .Select(h => (h.TriggerType, h.Kind, h.Destination)).OrderBy(h => h.TriggerType.Name).ToList();
            CollectionAssert.AreEqual(new[]
            {
                (typeof(PumpStart), HandlerKind.PermitIf, (Type?)typeof(PumpRunningState)),
                (typeof(PumpStop), HandlerKind.IgnoreIf, (Type?)null),
            }, handlers);
        }

        [TestMethod]
        public void GetHandlers_ReportsPermitReentryIf()
        {
            var kinds = StateHandlerScanner.GetHandlers(typeof(PumpRunningState)).ToDictionary(h => h.TriggerType, h => h.Kind);
            Assert.AreEqual(HandlerKind.PermitReentryIf, kinds[typeof(PumpTick)]);
            Assert.AreEqual(HandlerKind.PermitReentryIf, kinds[typeof(PumpReset)]);
            Assert.AreEqual(HandlerKind.PermitIf, kinds[typeof(PumpStop)]);
        }

        [TestMethod]
        public void GetFirstSubstateType_FindsIFirstSubstateChild()
        {
            Assert.AreEqual(typeof(PumpRunningSlowState),
                StateHandlerScanner.GetFirstSubstateType(typeof(PumpRunningState), typeof(ParentedBaseStateMachineState<,>)));
            Assert.IsNull(StateHandlerScanner.GetFirstSubstateType(typeof(PumpIdleState), typeof(ParentedBaseStateMachineState<,>)));
        }

        [TestMethod]
        public void GetEventTypes_FindsPublishEventGenericArgs()
        {
            CollectionAssert.Contains(StateHandlerScanner.GetEventTypes(typeof(PumpRunningState), "PublishEvent").ToList(), typeof(PumpStartedEvent));
        }
    }
}
