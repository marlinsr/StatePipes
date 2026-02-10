using Autofac;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Internal;

namespace StatePipes.StateMachine.Test.Internal
{
    internal class DummyContainerSetup<StateMachineType>(IDummyDependencyRegistration dummyRegisterator) : IContainerSetup where StateMachineType : IStateMachine
    {
        private readonly BaseDummyContainerSetup _baseDummyContainerSetup = new(typeof(StateMachineType), dummyRegisterator);

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
