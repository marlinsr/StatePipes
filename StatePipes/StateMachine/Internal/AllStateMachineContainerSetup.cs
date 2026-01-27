using Autofac;
using Autofac.Util;
using StatePipes.Interfaces;
using System.Reflection;

namespace StatePipes.StateMachine.Internal
{
    internal class AllStateMachineContainerSetup : IContainerSetup
    {
        private readonly List<BaseStateMachineContainerSetup> _stateMachineContainerSetup;
        public AllStateMachineContainerSetup(Assembly assembly, bool sendInitAfterInitialize = true)
        {
            _stateMachineContainerSetup = [];
            var baseStateMachineType = typeof(IStateMachine);
            assembly.GetLoadableTypes().Where(t => baseStateMachineType.IsAssignableFrom(t) && !t.Equals(baseStateMachineType)).ToList()
                .ForEach(smt => _stateMachineContainerSetup.Add(new BaseStateMachineContainerSetup(smt, sendInitAfterInitialize)));
        }
        public void Register(ContainerBuilder containerBuilder)
        {
            BaseStateMachineAndFirstStateContainerSetup.RegisterOnce(containerBuilder);
            _stateMachineContainerSetup.ForEach(sb => sb.Register(containerBuilder));
        }
        public void Build(IContainer container)
        {
            _stateMachineContainerSetup.ForEach(sb => sb.Build(container));
        }
    }
}
