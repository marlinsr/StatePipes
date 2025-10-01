using StatePipes.Interfaces;
using System.Collections.Concurrent;
namespace StatePipes.Common
{
    public abstract class TaskWrapper<T> : ITaskWrapper<T> //Assumption is T is immutable
    {
        protected readonly CancellationTokenSource _cancelTokenSource = new();
        protected Task? _task;
        protected bool _disposed;
        protected readonly ConcurrentQueue<T> _queue = new();
        protected readonly ManualResetEventSlim _resetEventSlim = new();
        protected readonly ManualResetEventSlim _startEventSlim = new();
        protected readonly ManualResetEventSlim _flushEventSlim = new(true);
        protected readonly object _lock = new();
        protected readonly object _flushLock = new();
        public void Queue(T obj, int signalHighWaterMark = 1)
        {
            lock (_lock)
            {
                _flushEventSlim.Wait(Timeout.Infinite);
                _queue.Enqueue(obj);
                if(_queue.Count >= signalHighWaterMark) _resetEventSlim.Set();
            }
        }
        public void ForceSignal()
        {
            lock (_lock)
            {
                _resetEventSlim.Set();
            }
        }
        public void Flush(CancellationToken token, int timeoutMilliseconds = Timeout.Infinite)
        {
            lock (_flushLock)
            {
                _flushEventSlim.Reset();
                _resetEventSlim.Set();
                _flushEventSlim.Wait(timeoutMilliseconds, token);
            }
        }
        public void Flush(int timeoutMilliseconds = Timeout.Infinite)
        {
            lock(_flushLock)
            {
                _flushEventSlim.Reset();
                _resetEventSlim.Set();
                _flushEventSlim.Wait(timeoutMilliseconds);
            }
        }
        protected T? GetNext()
        {
            PerformCancellation();
            lock (_lock)
            {
                _queue.TryDequeue(out T? result);
                if (result == null) _resetEventSlim.Reset();
                if (GetNumberOfItemsQueued() == 0) _flushEventSlim.Set();
                return result;
            }
        }
        protected List<T>? GetAll()
        {
            PerformCancellation();
            lock (_lock)
            {
                if(GetNumberOfItemsQueued() == 0 )
                {
                    _resetEventSlim.Reset();
                    _flushEventSlim.Set();
                    return null;
                }
                var result = _queue.ToList();
                _queue.Clear();
                _resetEventSlim.Reset();
                _flushEventSlim.Set();
                return result;
            }
        }
        protected List<T>? WaitGetAll(int timeoutMilliseconds)
        {
            PerformCancellation();
            var wasCanceled = !_resetEventSlim.Wait(timeoutMilliseconds, _cancelTokenSource.Token);
            return GetAll();
        }
        protected List<T>? WaitGetAll() => WaitGetAll(Timeout.Infinite);
        protected T? WaitGetNext() => WaitGetNext(Timeout.Infinite);
        protected T? WaitGetNext(int timeoutMilliseconds)
        {
            PerformCancellation();
            var wasCanceled = !_resetEventSlim.Wait(timeoutMilliseconds, _cancelTokenSource.Token);
            if (wasCanceled) return default;
            return GetNext();
        }
        public int GetNumberOfItemsQueued() => _queue.Count;
        public void Cancel()
        {
            if (_disposed) return;
            _cancelTokenSource.Cancel();
            if (_task != null)
            {
                try
                {
                    _ = _task.Wait(1000);
                }
                catch (Exception) { }
                _task = null;
            }
            _startEventSlim.Reset();
        }
        protected void PerformCancellation()
        {
            if (_cancelTokenSource.Token.IsCancellationRequested)
            {
                _cancelTokenSource.Token.ThrowIfCancellationRequested();
            }
        }
        public void StartAndWait()
        {
            StartShortRunning();
            _startEventSlim.Wait();
        }
        public void StartShortRunning() => _task = Task.Factory.StartNew(() => Run(), _cancelTokenSource.Token);
        public void StartLongRunningAndWait()
        {
            StartLongRunning();
            _startEventSlim.Wait();
        }
        public void StartLongRunning() => _task = Task.Factory.StartNew(() => Run(), _cancelTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        private void Run()
        {
            try
            {
                PerformCancellation();
                _startEventSlim.Set();
                DoWork();
            }
            catch (Exception)
            {
            }
        }
        protected abstract void DoWork();
        public virtual void Dispose()
        {
            if (_disposed) return;
            GC.SuppressFinalize(this);
            _disposed = true;
            try { Cancel(); } catch (Exception) { }
            try { _cancelTokenSource.Dispose(); } catch (Exception) { }
            try { _resetEventSlim.Dispose(); } catch (Exception) { }
            try { _startEventSlim.Dispose(); } catch (Exception) { }
        }
    }
}
