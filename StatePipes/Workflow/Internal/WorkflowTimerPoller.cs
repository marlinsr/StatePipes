using Newtonsoft.Json;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using System.Reflection;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.Workflow.Internal
{
    /// <summary>
    /// Fires due persisted workflow timers by putting their messages on this service's command queue, flagged as timer
    /// deliveries. One per replica; SKIP LOCKED in the store keeps replicas from firing the same timer.
    /// </summary>
    internal sealed class WorkflowTimerPoller(IWorkflowStore store, IReadOnlyCollection<string> workflowTypes, ICommandRequeuer requeuer,
        BusConfig replyTo, JsonSerializerSettings settings, TimeSpan pollInterval, Assembly serviceAssembly, IMessageSender? eventSender = null) : IDisposable
    {
        public const int BatchSize = 100;
        private readonly HashSet<Guid> _reported = [];
        private readonly ManualResetEventSlim _stop = new(false);
        private Thread? _thread;

        public int PollOnce()
        {
            Guid[] excluded;
            lock (_reported) excluded = [.. _reported];
            return store.FireDueTimers(workflowTypes, BatchSize, Send, excluded);
        }

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = nameof(WorkflowTimerPoller) };
            _thread.Start();
        }
        private void Run()
        {
            while (!_stop.Wait(pollInterval))
            {
                try { PollOnce(); }
                catch (Exception ex)
                {
                    Log?.LogException(ex);
                    _stop.Wait(pollInterval);
                }
            }
        }
        private bool Send(TimerRecord timer)
        {
            object? message = null;
            try
            {
                var type = serviceAssembly.GetType(timer.MessageTypeFullName) ?? Type.GetType(timer.MessageTypeFullName);
                if (type != null) message = JsonConvert.DeserializeObject(timer.MessageJson, type, settings);
            }
            catch (Exception ex) { Report(timer, ex.Message); return false; }
            if (message == null) { Report(timer, "message type could not be resolved"); return false; }
            if (message is IEvent)
            {
                // Timed events are published, as singleton timers do; only commands go through the workflow engine.
                if (eventSender == null) { Report(timer, "no event publisher for an event timer message"); return false; }
                eventSender.SendMessage((IMessage)message);
                return true;
            }
            requeuer.Requeue(message, replyTo, 0, isTimerDelivery: true);
            return true;
        }
        private void Report(TimerRecord timer, string reason)
        {
            lock (_reported) if (!_reported.Add(timer.TimerId)) return;
            Log?.LogError($"Workflow timer {timer.TimerId} ({timer.MessageTypeFullName}, workflow={timer.WorkflowType}, correlationId={timer.CorrelationId}) left in place: {reason}");
        }
        public void Dispose()
        {
            _stop.Set();
            _thread?.Join(TimeSpan.FromSeconds(5));
        }
    }
}
