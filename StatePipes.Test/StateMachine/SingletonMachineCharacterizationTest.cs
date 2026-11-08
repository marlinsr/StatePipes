using Autofac;
using Microsoft.Extensions.Time.Testing;
using StatePipes.StateMachine.Test;
using StatePipes.Test.Common;
using StatePipes.Test.StateMachine.Samples;

namespace StatePipes.Test.StateMachine
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class SingletonMachineCharacterizationTest
    {
        private static StateMachineForTest Create(FakeTimeProvider? time = null)
        {
            var sm = new StateMachineForTest { TimeProvider = time };
            sm.ConfigureStateMachine<PumpMachine>(new NoDummies());
            sm.Start();
            return sm;
        }

        [TestMethod]
        public void PermitIf_Transitions()
        {
            using var sm = Create();
            sm.Fire(new PumpStart());
            Assert.AreEqual(nameof(PumpRunningSlowState), sm.CurrentState);
        }

        [TestMethod]
        public void PermitIf_GuardFalse_Stays()
        {
            using var sm = Create();
            sm.Fire(new PumpStart { Block = true });
            Assert.IsTrue(sm.IsCurrentState<PumpIdleState>());
        }

        [TestMethod]
        public void IgnoreIf_Consumes()
        {
            using var sm = Create();
            sm.Fire(new PumpStop());
            Assert.IsTrue(sm.IsCurrentState<PumpIdleState>());
        }

        [TestMethod]
        public void PermitReentryIfFalse_RunsBodyNoEntryExit()
        {
            using var sm = Create();
            sm.Fire(new PumpStart());
            var running = sm.Container!.Resolve<PumpRunningState>();
            sm.Fire(new PumpTick());
            Assert.AreEqual(1, running.TickCount);
            Assert.AreEqual(1, running.EntryCount);
            Assert.AreEqual(0, running.ExitCount);
            Assert.AreEqual(nameof(PumpRunningSlowState), sm.CurrentState);
        }

        [TestMethod]
        public void PermitReentryIfTrue_ReentersParent_MovesToFirstSubstate()
        {
            using var sm = Create();
            sm.Fire(new PumpStart());
            sm.Fire(new PumpFaster());
            Assert.AreEqual(nameof(PumpRunningFastState), sm.CurrentState);
            sm.Fire(new PumpReset { Reenter = true });
            Assert.AreEqual(nameof(PumpRunningSlowState), sm.CurrentState);
        }

        [TestMethod]
        public void PeriodicTimer_FiresTriggerWithFakeTime()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using var sm = Create(time);
            sm.Fire(new PumpStart { StartTicking = true });
            var running = sm.Container!.Resolve<PumpRunningState>();
            for (int i = 1; i <= 3; i++)
            {
                time.Advance(TimeSpan.FromSeconds(1));
                InMemoryTimerServiceTest.WaitFor(() => running.TickCount == i);
            }
            Assert.AreEqual(3, running.TickCount);
        }
    }
}
