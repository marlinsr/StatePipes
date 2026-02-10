using Autofac;
using StatePipes.Interfaces;
namespace StatePipes.StateMachine.Internal
{
    internal class StateMachineAndFirstStateContainerSetup<StateMachineType, NextStateAfterInit>(bool disableAutomaticMoveToState = true) : IContainerSetup where StateMachineType : IStateMachine where NextStateAfterInit : IStateMachineState
    {
        private readonly BaseStateMachineAndFirstStateContainerSetup _baseContainerSetup = new(typeof(StateMachineType), typeof(NextStateAfterInit), false, disableAutomaticMoveToState);

        public void Register(ContainerBuilder containerBuilder)
        {
            BaseStateMachineAndFirstStateContainerSetup.RegisterOnce(containerBuilder);
            _baseContainerSetup.Register(containerBuilder);
        }
        public void Build(IContainer container) => _baseContainerSetup.Build(container);
    }
}
