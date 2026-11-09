using Autofac;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StatePipes.Common.Internal;
using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Test.Internal;
using StatePipes.Workflow.Internal;

namespace StatePipes.StateMachine.Test
{
    /// <summary>
    /// Runs one workflow type against an in-memory store and a manual clock, with no PostgreSQL. Every command still goes
    /// through the full persist / rehydrate cycle, so field-persistence mistakes show up here.
    /// </summary>
    public sealed class WorkflowForTest<TWorkflow> : IDisposable where TWorkflow : IWorkflow
    {
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(10);
        private readonly string _workflowType = typeof(TWorkflow).FullName!;
        private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        private readonly TestStatePipesService _bus = new();
        private readonly InMemoryTimerService _timers;
        private readonly InMemoryWorkflowStore _store;
        private readonly AllWorkflowContainerSetup _setup;
        private readonly IContainer _container;
        private readonly JsonSerializerSettings _settings = WorkflowJson.CreateSettings(typeof(TWorkflow).Assembly);

        public WorkflowForTest(int lockedMaxRetries = 10, TimeSpan? lockedRetryDelay = null)
        {
            _timers = new InMemoryTimerService(_bus, _time);
            _store = new InMemoryWorkflowStore(_time);
            var config = new WorkflowStoreConfig("in-memory", "in-memory", "in-memory", "none",
                lockedMaxRetries: lockedMaxRetries, lockedRetryDelay: lockedRetryDelay);
            _setup = new AllWorkflowContainerSetup(typeof(TWorkflow).Assembly, _ => _store, config, null,
                t => t == typeof(TWorkflow), startPoller: false);
            var builder = new ContainerBuilder();
            // Registered first so the workflow handlers registered after it stay the defaults.
            builder.RegisterGeneric(typeof(NoOpMessageHandler<>)).As(typeof(IMessageHandler<>));
            builder.RegisterInstance(_bus).As<IStatePipesService>().ExternallyOwned();
            builder.RegisterInstance(_timers).AsSelf().ExternallyOwned();
            _setup.Register(builder);
            _container = builder.Build();
            _bus.SetContainer(_container);
            _setup.Build(_container);
        }

        public void SendCommand<TCommand>(TCommand command) where TCommand : class, ICommand
        {
            _bus.SendCommand(command);
            Drain();
        }
        public string? CurrentState(string correlationId) => Find(correlationId)?.CurrentState;
        public bool Exists(string correlationId) => Find(correlationId) != null;

        /// <summary>Reads one persisted field of one state from the stored document.</summary>
        public TField? GetField<TState, TField>(string correlationId, string fieldName)
        {
            var record = Find(correlationId) ?? throw new InvalidOperationException($"No workflow instance {correlationId}.");
            var token = JObject.Parse(record.DocumentJson)["states"]?[typeof(TState).Name]?[fieldName];
            if (token == null) return default;
            WorkflowContext.Current = new WorkflowContext(null!, correlationId, _timers);
            try { return (TField?)JsonConvert.DeserializeObject(token.ToString(Formatting.None), typeof(TField), _settings); }
            finally { WorkflowContext.Current = null; }
        }

        /// <summary>Moves the clock forward, stopping at every due timer on the way so they fire in order.</summary>
        public void AdvanceTime(TimeSpan by)
        {
            var target = _time.GetUtcNow() + by;
            Drain();
            for (int guard = 0; guard < 1_000_000; guard++)
            {
                var now = _time.GetUtcNow();
                var next = Earliest(InMemoryNextDue(), _store.NextExpiration([_workflowType]));
                if (next is not DateTimeOffset due || due > target) break;
                _time.SetUtcNow(due);
                Drain();
                var after = Earliest(InMemoryNextDue(), _store.NextExpiration([_workflowType]));
                if (due <= now && after == next) break; // a due timer that cannot be delivered; stop rather than spin
            }
            _time.SetUtcNow(target);
            Drain();
        }

        public void SimulateLocked(string correlationId) => _store.SimulateLock(_workflowType, correlationId);
        public void ReleaseLock(string correlationId) => _store.ReleaseSimulatedLock(_workflowType, correlationId);
        public List<ICommand> CommandInvocations => _bus.CommandInvocations;
        public List<IEvent> EventInvocations => _bus.EventInvocations;
        public void FilterCommand<T>(int skip = 0, int block = int.MaxValue) where T : class, ICommand => _bus.FilterCommand<T>(skip, block);
        public TimedBlockOnFilter<ICommand>? TrapCommand<T>(int skip, int timeoutMsec, bool stopProcessingCmdsWhenTriggered = false) where T : class, ICommand =>
            _bus.TrapCommand<T>(skip, timeoutMsec, stopProcessingCmdsWhenTriggered);

        private WorkflowRecord? Find(string correlationId) =>
            _store.Instances.FirstOrDefault(i => i.WorkflowType == _workflowType && i.CorrelationId == correlationId);

        private DateTimeOffset? InMemoryNextDue() => _timers.TimeUntilNextDue is TimeSpan remaining ? _time.GetUtcNow() + remaining : null;

        private static DateTimeOffset? Earliest(DateTimeOffset? a, DateTimeOffset? b) =>
            a is null ? b : b is null ? a : (a < b ? a : b);

        /// <summary>Runs until no command, due in-memory timer, or due persisted timer is left to process.</summary>
        private void Drain()
        {
            do WaitUntil(() => !_timers.HasDueWork && _bus.IsIdle);
            while (_setup.Registry.Poller!.PollOnce() > 0);
        }

        private static void WaitUntil(Func<bool> condition)
        {
            if (!SpinWait.SpinUntil(condition, IdleTimeout))
                throw new TimeoutException("Workflow test bus did not become idle (is command processing stopped by a trap?).");
        }

        public void Dispose()
        {
            _container.Dispose();
            _timers.Dispose();
            _bus.Dispose();
        }
    }
}
