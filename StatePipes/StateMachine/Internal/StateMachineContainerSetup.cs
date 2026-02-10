using Autofac;
using StatePipes.Interfaces;
namespace StatePipes.StateMachine.Internal
{
    internal class StateMachineContainerSetup<StateMachineType>(bool disableAutomaticMoveToState = true) : IContainerSetup where StateMachineType : IStateMachine
    {
        private readonly BaseStateMachineContainerSetup _baseContainerSetup = new(typeof(StateMachineType), false, disableAutomaticMoveToState);

        public void Register(ContainerBuilder containerBuilder)
        {
            BaseStateMachineAndFirstStateContainerSetup.RegisterOnce(containerBuilder);
            _baseContainerSetup.Register(containerBuilder);
        }
        public void Build(IContainer container) => _baseContainerSetup.Build(container);
    }
}
