using Autofac;
using Autofac.Util;
using StatePipes.Interfaces;
using System.Reflection;

namespace StatePipes.StateMachine.Internal
{
    internal class AllStateMachineDummyContainerSetup : IContainerSetup
    {
        private readonly List<BaseDummyContainerSetup> _stateMachineDummyBinding;
        public AllStateMachineDummyContainerSetup(Assembly assembly, IDummyDependencyRegistration dummyRegisterator)
        {
            _stateMachineDummyBinding = [];
            var baseStateMachineType = typeof(IStateMachine);
            assembly.GetLoadableTypes().Where(t => baseStateMachineType.IsAssignableFrom(t) && !t.Equals(baseStateMachineType)).ToList()
                .ForEach(smt => _stateMachineDummyBinding.Add(new BaseDummyContainerSetup(smt, dummyRegisterator)));
        }
        public void Build(IContainer container)
        {
            _stateMachineDummyBinding.ForEach(sb => sb.Build(container));
        }

        public void Register(ContainerBuilder containerBuilder)
        {
            containerBuilder.RegisterType<StateMachineManager>().AsSelf().SingleInstance();
            _stateMachineDummyBinding.ForEach(sb => sb.Register(containerBuilder));
        }
    }
}
