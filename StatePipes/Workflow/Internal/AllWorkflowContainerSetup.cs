using Autofac;
using Autofac.Util;
using StatePipes.Common.Internal;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using System.Reflection;

namespace StatePipes.Workflow.Internal
{
    /// <summary>
    /// Discovers and validates the workflows in a service assembly, registers a handler per workflow trigger, and on build
    /// creates the store, the engines and the timer poller. A service without workflows never touches the store.
    /// </summary>
    internal sealed class AllWorkflowContainerSetup(Assembly assembly, Func<WorkflowStoreConfig?, IWorkflowStore> storeFactory,
        WorkflowStoreConfig? config, BusConfig? ownBusConfig, Func<Type, bool>? workflowFilter = null, bool startPoller = true) : IContainerSetup
    {
        private readonly WorkflowEngineRegistry _registry = new();
        private List<WorkflowMetadata> _workflows = [];
        public WorkflowEngineRegistry Registry => _registry;
        public IWorkflowStore? Store { get; private set; }

        public void Register(ContainerBuilder containerBuilder)
        {
            _workflows = [.. assembly.GetLoadableTypes()
                .Where(t => typeof(IWorkflow).IsAssignableFrom(t) && t != typeof(IWorkflow) && (workflowFilter?.Invoke(t) ?? true))
                .Select(WorkflowMetadata.Build)];
            if (_workflows.Count == 0) return;
            if (config == null) throw new WorkflowValidationException("Service declares workflows but ServiceConfiguration.WorkflowStore is not set.");
            containerBuilder.RegisterInstance(_registry).AsSelf();
            foreach (var md in _workflows)
                foreach (var trigger in md.TriggerTypes)
                    containerBuilder.RegisterType(typeof(BaseWorkflowTriggerCommandHandler<,>).MakeGenericType(trigger, md.WorkflowType))
                        .AsImplementedInterfaces().SingleInstance();
        }

        public void Build(IContainer container)
        {
            if (_workflows.Count == 0) return;
            var store = storeFactory(config);
            Store = store;
            _registry.OwnedStore = store as IDisposable;
            store.EnsureSchema();
            var bus = container.Resolve<IStatePipesService>();
            var requeuer = bus as ICommandRequeuer ?? throw new InvalidOperationException("The service bus cannot requeue commands.");
            var timers = container.Resolve<InMemoryTimerService>();
            var settings = WorkflowJson.CreateSettings(assembly);
            var options = new WorkflowEngineOptions { LockedMaxRetries = config!.LockedMaxRetries, LockedRetryDelay = config.LockedRetryDelay };
            foreach (var md in _workflows) _registry.Add(md.WorkflowType, new WorkflowEngine(md, store, bus, requeuer, timers, settings, options));
            _registry.Poller = new WorkflowTimerPoller(store, [.. _workflows.Select(w => w.WorkflowTypeName)], requeuer,
                ownBusConfig ?? new BusConfig(string.Empty, string.Empty, string.Empty, string.Empty), settings, config.TimerPollInterval, assembly);
            if (startPoller) _registry.Poller.Start();
        }
    }
}
