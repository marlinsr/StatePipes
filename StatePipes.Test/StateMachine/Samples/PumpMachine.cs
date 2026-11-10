using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.StateMachine;

namespace StatePipes.Test.StateMachine.Samples
{
    public interface PumpMachine : IStateMachine { }

    public class PumpStart : BaseTriggerCommand<PumpMachine> { public bool Block { get; init; } public bool StartTicking { get; init; } }
    public class PumpStop : BaseTriggerCommand<PumpMachine> { }
    public class PumpTick : BaseTriggerCommand<PumpMachine> { }
    public class PumpReset : BaseTriggerCommand<PumpMachine> { public bool Reenter { get; init; } }
    public class PumpFaster : BaseTriggerCommand<PumpMachine> { }

    public class PumpStartedEvent : IEvent { }

    internal class PumpIdleState : BaseStateMachineState<PumpMachine>, IFirstStateForStateMachine
    {
        [PermitIf(typeof(PumpRunningState))]
        private bool OnStart(PumpStart cmd, BusConfig? r) => !cmd.Block;

        [IgnoreIf]
        private bool OnStop(PumpStop cmd, BusConfig? r) => true;
    }

    internal class PumpRunningState : BaseStateMachineState<PumpMachine>
    {
        private IDelayedMessageSender<PumpTick>? _ticker;
        public int EntryCount { get; private set; }
        public int ExitCount { get; private set; }
        public int TickCount { get; private set; }

        [PermitIf(typeof(PumpIdleState))]
        private bool OnStop(PumpStop cmd, BusConfig? r) => true;

        [PermitReentryIf]
        private bool OnReset(PumpReset cmd, BusConfig? r) => cmd.Reenter;

        [PermitReentryIf]
        private bool OnTick(PumpTick cmd, BusConfig? r)
        {
            TickCount++;
            return false;
        }

        public override void OnEntry()
        {
            EntryCount++;
            PublishEvent(new PumpStartedEvent());
            if (GetCurrentTrigger<PumpStart>()?.StartTicking == true)
            {
                _ticker ??= CreateDelayedMessageSender<PumpTick>();
                _ticker?.StartPeriodic(TimeSpan.FromSeconds(1), new PumpTick());
            }
        }
        public override void OnExit()
        {
            ExitCount++;
            _ticker?.Stop();
        }
    }

    internal class PumpRunningSlowState : ParentedBaseStateMachineState<PumpMachine, PumpRunningState>, IFirstSubstate
    {
        [PermitIf(typeof(PumpRunningFastState))]
        private bool OnFaster(PumpFaster cmd, BusConfig? r) => true;
    }

    internal class PumpRunningFastState : ParentedBaseStateMachineState<PumpMachine, PumpRunningState> { }

    internal class NoDummies : IDummyDependencyRegistration
    {
        public void RegisterDummyForType(Autofac.ContainerBuilder builder, Type t) { }
    }
}
