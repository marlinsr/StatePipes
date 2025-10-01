using Autofac;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Internal;

namespace StatePipes.StateMachine.Test.Internal
{
    internal class DummyContainerSetup<StateMachineType> : IContainerSetup where StateMachineType : IStateMachine
    {
        private readonly BaseDummyContainerSetup _baseDummyContainerSetup;
        public DummyContainerSetup(IDummyDependencyRegistration dummyRegisterator)
        {
            _baseDummyContainerSetup = new BaseDummyContainerSetup(typeof(StateMachineType),dummyRegisterator);
        }
        public void Build(IContainer container)
        {
            _baseDummyContainerSetup.Build(container);
        }

        public void Register(ContainerBuilder containerBuilder)
        {
            _baseDummyContainerSetup.Register(containerBuilder);
        }
    }
}
