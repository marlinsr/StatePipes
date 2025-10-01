using Autofac;
using StatePipes.Interfaces;
namespace StatePipes.StateMachine.Internal
{
    internal class StateMachineAndFirstStateContainerSetup<StateMachineType, NextStateAfterInit> : IContainerSetup where StateMachineType : IStateMachine where NextStateAfterInit : IStateMachineState
    {
        private readonly BaseStateMachineAndFirstStateContainerSetup _baseContainerSetup;
        public StateMachineAndFirstStateContainerSetup(bool disableAutomaticMoveToState = true)
        {
            _baseContainerSetup = new BaseStateMachineAndFirstStateContainerSetup(typeof(StateMachineType), typeof(NextStateAfterInit), false, disableAutomaticMoveToState);
        }
        public void Register(ContainerBuilder containerBuilder)
        {
            BaseStateMachineAndFirstStateContainerSetup.RegisterOnce(containerBuilder);
            _baseContainerSetup.Register(containerBuilder);
        }
        public void Build(IContainer container) => _baseContainerSetup.Build(container);
    }
}
