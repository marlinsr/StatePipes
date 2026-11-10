using Autofac;
using Autofac.Util;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Test.Internal;
using StatePipes.Workflow.Internal;
using System.Reflection;

namespace StatePipes.StateMachine.Internal
{
    /// <summary>
    /// Diagram-only container setup (StatePipes.Diagrammer): singleton machines get dummy dependencies, and each workflow
    /// gets a machine built from fresh state objects with no database so it can be drawn alongside them.
    /// </summary>
    internal class AllStateMachineDummyContainerSetup : IContainerSetup
    {
        private readonly List<BaseDummyContainerSetup> _stateMachineDummyBinding;
        private readonly List<Type> _workflowTypes;
        public AllStateMachineDummyContainerSetup(Assembly assembly, IDummyDependencyRegistration dummyRegisterator) : this(assembly, dummyRegisterator, null) { }
        public AllStateMachineDummyContainerSetup(Assembly assembly, IDummyDependencyRegistration dummyRegisterator, Func<Type, bool>? workflowFilter)
        {
            _stateMachineDummyBinding = [];
            AllStateMachineContainerSetup.GetSingletonStateMachineTypes(assembly).ToList()
                .ForEach(smt => _stateMachineDummyBinding.Add(new BaseDummyContainerSetup(smt, dummyRegisterator)));
            _workflowTypes = [.. assembly.GetLoadableTypes()
                .Where(t => typeof(IWorkflow).IsAssignableFrom(t) && t != typeof(IWorkflow) && (workflowFilter?.Invoke(t) ?? true))];
        }
        public void Build(IContainer container)
        {
            _stateMachineDummyBinding.ForEach(sb => sb.Build(container));
            if (_workflowTypes.Count == 0) return;
            var manager = container.Resolve<StateMachineManager>();
            var bus = container.ResolveOptional<IStatePipesService>() ?? new TestStatePipesService();
            using var timers = new InMemoryTimerService(bus, TimeProvider.System);
            foreach (var workflowType in _workflowTypes)
                manager.RegisterStateMachine(workflowType, WorkflowMachineFactory.CreateForDiagram(WorkflowMetadata.Build(workflowType), bus, timers).Machine);
        }

        public void Register(ContainerBuilder containerBuilder)
        {
            containerBuilder.RegisterType<StateMachineManager>().AsSelf().SingleInstance();
            _stateMachineDummyBinding.ForEach(sb => sb.Register(containerBuilder));
        }
    }
}
