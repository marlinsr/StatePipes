using Newtonsoft.Json;
using StatePipes.Common.Internal;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.Workflow.Internal
{
    internal sealed class WorkflowEngineOptions
    {
        public int LockedMaxRetries { get; init; } = 10;
        public TimeSpan LockedRetryDelay { get; init; } = TimeSpan.FromMilliseconds(50);
    }

    /// <summary>
    /// Handles one workflow command end to end (spec §4.3): lock the instance for this transaction, rehydrate or create it,
    /// fire, then save, delete (final state) or roll back.
    /// </summary>
    internal sealed class WorkflowEngine(WorkflowMetadata md, IWorkflowStore store, IStatePipesService bus, ICommandRequeuer requeuer,
        InMemoryTimerService timers, JsonSerializerSettings settings, WorkflowEngineOptions options)
    {
        private readonly string _type = md.WorkflowTypeName;

        public void Handle(ITrigger command, string correlationId, BusConfig? replyTo, int retryCount, bool isTimerDelivery)
        {
            var commandName = command.GetType().FullName;
            if (string.IsNullOrEmpty(correlationId))
            {
                Log?.LogError($"Workflow command {commandName} for {_type} has no CorrelationId; discarded.");
                return;
            }
            var where = $"(workflow={_type}, correlationId={correlationId}, command={commandName})";
            try
            {
                using var uow = store.Begin();
                if (!uow.TryLock(_type, correlationId))
                {
                    OnLocked(command, correlationId, replyTo, retryCount, isTimerDelivery, where);
                    return;
                }
                var record = uow.Load(_type, correlationId);
                if (record == null && isTimerDelivery)
                {
                    Log?.LogInfo($"Workflow timer message for a finished or unknown instance discarded {where}");
                    return;
                }
                if (record != null && !md.StateTypes.Any(t => t.Name == record.CurrentState))
                {
                    Log?.LogError($"Workflow instance is in unknown state {record.CurrentState}; command discarded, row left for repair {where}");
                    return;
                }
                var context = new WorkflowContext(null!, correlationId, new WorkflowTimerService(uow, _type, correlationId, settings));
                var wm = record == null
                    ? WorkflowMachineFactory.CreateNew(md, context, bus, timers)
                    : WorkflowMachineFactory.Rehydrate(md, record, context, bus, timers, settings);
                if (!md.IsDeclared(wm.CurrentState, command.GetType()))
                {
                    Log?.LogVerbose($"Workflow command not handled in state {wm.CurrentState}; rolled back {where}");
                    return;
                }
                WorkflowMachineFactory.Fire(wm, command, replyTo);
                if (md.IsFinal(wm.CurrentState)) uow.DeleteInstanceAndTimers(_type, correlationId);
                else uow.Upsert(_type, correlationId, wm.CurrentState, WorkflowDocument.Extract(md, wm.StatesByName, settings));
                uow.Commit();
            }
            catch (Exception ex)
            {
                Log?.LogError($"Workflow command failed; rolled back and discarded {where}: {ex.GetBaseException().Message}");
                Log?.LogException(ex);
            }
            finally
            {
                WorkflowContext.Current = null;
            }
        }

        private void OnLocked(ITrigger command, string correlationId, BusConfig? replyTo, int retryCount, bool isTimerDelivery, string where)
        {
            if (retryCount >= options.LockedMaxRetries)
            {
                Log?.LogWarning($"Workflow command not satisfied: workflow locked (workflow={_type}, correlationId={correlationId}, command={command.GetType().FullName}, retries={retryCount})");
                return;
            }
            var config = replyTo ?? new BusConfig(string.Empty, string.Empty, string.Empty, string.Empty);
            timers.ScheduleCallback(options.LockedRetryDelay, () => requeuer.Requeue(command, config, retryCount + 1, isTimerDelivery));
            Log?.LogVerbose($"Workflow instance locked; requeued (retry {retryCount + 1}) {where}");
        }
    }
}
