using Autofac;
using StatePipes.Interfaces;
namespace StatePipes.StateMachine.Internal
{
    internal class StateMachineContainerSetup<StateMachineType> : IContainerSetup where StateMachineType : IStateMachine
    {
        private readonly BaseStateMachineContainerSetup _baseContainerSetup;
        public StateMachineContainerSetup(bool disableAutomaticMoveToState = true)
        {
            _baseContainerSetup = new BaseStateMachineContainerSetup(typeof(StateMachineType), false, disableAutomaticMoveToState);
        }
        public void Register(ContainerBuilder containerBuilder)
        {
            BaseStateMachineAndFirstStateContainerSetup.RegisterOnce(containerBuilder);
            _baseContainerSetup.Register(containerBuilder);
        }
        public void Build(IContainer container) => _baseContainerSetup.Build(container);
    }
}
