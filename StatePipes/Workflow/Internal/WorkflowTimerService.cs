using Newtonsoft.Json;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;

namespace StatePipes.Workflow.Internal
{
    /// <summary>Workflow timers: rows in the workflow_timer table written inside the instance's own transaction (spec §6.4).</summary>
    internal sealed class WorkflowTimerService(IWorkflowUnitOfWork unitOfWork, string workflowType, string correlationId, JsonSerializerSettings settings) : ITimerService
    {
        public void StartOneShot(Guid timerId, TimeSpan dueTime, IMessage message) => Start(timerId, dueTime, null, message);
        public void StartPeriodic(Guid timerId, TimeSpan period, IMessage message) => Start(timerId, period, period, message);
        public void Stop(Guid timerId) => unitOfWork.DeleteTimer(timerId);
        public bool IsEnabled(Guid timerId) => unitOfWork.TimerExists(timerId);
        private void Start(Guid timerId, TimeSpan dueTime, TimeSpan? period, IMessage message)
        {
            var type = message.GetType();
            unitOfWork.UpsertTimer(new TimerRecord(timerId, workflowType, correlationId, period, type.FullName!,
                JsonConvert.SerializeObject(message, type, settings)), dueTime);
        }
    }
}
