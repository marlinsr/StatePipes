namespace StatePipes.Interfaces
{
    public interface ITaskWrapper<T> : IDisposable //Assumption is T is immutable 
    {
        void Queue(T obj, int signalHighWaterMark = 1);
        void ForceSignal();
        void Flush(CancellationToken token, int timeoutMilliseconds = Timeout.Infinite);
        void Flush(int timeoutMilliseconds = Timeout.Infinite);
        int GetNumberOfItemsQueued();
        void Cancel();
        void StartAndWait();
        void StartShortRunning();
        void StartLongRunningAndWait();
        void StartLongRunning();
    }
}
