using Autofac;
using Autofac.Util;
using StatePipes.Interfaces;
using System.Reflection;

namespace StatePipes.StateMachine.Internal
{
    internal class AllStateMachineContainerSetup : IContainerSetup
    {
        private readonly List<BaseStateMachineContainerSetup> _stateMachineContainerSetup;
        public IReadOnlyList<Type> StateMachineTypes { get; }
        public AllStateMachineContainerSetup(Assembly assembly, bool sendInitAfterInitialize = true)
        {
            _stateMachineContainerSetup = [];
            StateMachineTypes = GetSingletonStateMachineTypes(assembly);
            StateMachineTypes.ToList().ForEach(smt => _stateMachineContainerSetup.Add(new BaseStateMachineContainerSetup(smt, sendInitAfterInitialize)));
        }
        /// <summary>Singleton state machine markers in the assembly; workflows are handled by AllWorkflowContainerSetup.</summary>
        internal static IReadOnlyList<Type> GetSingletonStateMachineTypes(Assembly assembly)
        {
            var baseStateMachineType = typeof(IStateMachine);
            return [.. assembly.GetLoadableTypes().Where(t => baseStateMachineType.IsAssignableFrom(t) && !t.Equals(baseStateMachineType) && !typeof(IWorkflow).IsAssignableFrom(t))];
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
