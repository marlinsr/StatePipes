using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.Explorer.NonWebClasses
{
    /// <summary>
    /// State of one WORKFLOW-tab request: pending id, reply detection, timeout, result and status text.
    /// Not thread-safe; the component calls it only on the Blazor renderer context.
    /// </summary>
    internal sealed class WorkflowInterrogationSession
    {
        public static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);
        private IEvent? _snapshot;
        private DateTime _sentAt;

        public string? PendingCorrelationId { get; private set; }
        public bool IsPending => PendingCorrelationId != null;
        public WorkflowDiagramsEvent? Result { get; private set; }
        public string Status { get; private set; } = string.Empty;

        /// <summary>Begins a request; <paramref name="snapshot"/> is the reply payload present when it was sent.</summary>
        public void Start(string correlationId, IEvent? snapshot, DateTime utcNow)
        {
            PendingCorrelationId = correlationId;
            _snapshot = snapshot;
            _sentAt = utcNow;
            Result = null;
            Status = "Waiting for reply…";
        }

        /// <summary>Checks the latest reply payload; returns true when the request finished (reply accepted or timed out).</summary>
        public bool Tick(IEvent? latestReply, DateTime utcNow)
        {
            var pending = PendingCorrelationId;
            if (pending == null) return false;
            if (WorkflowInterrogation.IsMatchingReply(latestReply, _snapshot, pending))
            {
                Result = WorkflowInterrogation.ToEvent(latestReply!);
                Status = WorkflowInterrogation.StatusText(pending, Result?.Workflows.Count ?? 0);
            }
            else if (utcNow - _sentAt >= ReplyTimeout) Status = "No reply after 10 s";
            else return false;
            PendingCorrelationId = null;
            return true;
        }

        public void Fail(string message)
        {
            PendingCorrelationId = null;
            Status = message;
        }
    }
}
