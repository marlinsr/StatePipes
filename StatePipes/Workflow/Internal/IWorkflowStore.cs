namespace StatePipes.Workflow.Internal
{
    internal sealed record WorkflowRecord(string WorkflowType, string CorrelationId, string CurrentState, string DocumentJson, long Version);

    internal sealed record TimerRecord(Guid TimerId, string WorkflowType, string CorrelationId, TimeSpan? Period, string MessageTypeFullName, string MessageJson);

    /// <summary>One workflow command's transaction. Disposing without <see cref="Commit"/> rolls back.</summary>
    internal interface IWorkflowUnitOfWork : IDisposable
    {
        bool TryLock(string workflowType, string correlationId);
        WorkflowRecord? Load(string workflowType, string correlationId);
        void Upsert(string workflowType, string correlationId, string currentState, string documentJson);
        void DeleteInstanceAndTimers(string workflowType, string correlationId);
        void UpsertTimer(TimerRecord timer, TimeSpan dueTime);
        void DeleteTimer(Guid timerId);
        bool TimerExists(Guid timerId);
        void Commit();
    }

    internal interface IWorkflowStore
    {
        void EnsureSchema();
        IWorkflowUnitOfWork Begin();
        /// <summary>Claims due timers of the given workflow types and hands each to <paramref name="send"/>.
        /// A one-shot is then deleted, a periodic rescheduled (skip-missed); <c>send</c> returning false leaves the row as is.
        /// Timers in <paramref name="excluded"/> (rows this process cannot deliver) are not claimed at all.</summary>
        int FireDueTimers(IReadOnlyCollection<string> workflowTypes, int batchSize, Func<TimerRecord, bool> send, IReadOnlyCollection<Guid>? excluded = null);
    }
}
