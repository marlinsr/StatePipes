using StatePipes.StateMachine.Test.Internal;

namespace StatePipes.StateMachine.Test
{
    public class TimedBlockOnFilter<A> : IDisposable
    {
        private readonly ManualResetEvent _event = new(false);
        private readonly int _timeoutMsec;
        private readonly object _lock = new();
        private readonly DefaultFilter<A> _filter;
        private bool _disposed;
        private A? _retObject;
        private readonly bool _triggerReturnValueWhenSignaled;
        private bool _isSignaled;
        internal TimedBlockOnFilter(DefaultFilter<A> filter, int timeoutMsec, bool triggerReturnValueWhenSignaled = false)
        {
            _filter = filter;
            _timeoutMsec = timeoutMsec;
            _triggerReturnValueWhenSignaled = triggerReturnValueWhenSignaled;
        }
        public bool TriggerReturnValue => _triggerReturnValueWhenSignaled && _isSignaled;
        public A? Wait()
        {
            if (_disposed) return default;
            bool ret = _event.WaitOne(_timeoutMsec);
            if (!ret) return default;
            return _retObject;
        }
        public void Dispose()
        {
            if (_disposed) return;
            GC.SuppressFinalize(this);
            _disposed = true;
            _event.Dispose();
        }
        public bool Trigger(A obj)
        {
            if (_disposed) return false;
            if (obj  == null) return false;
            lock (_lock)
            {
                if (_filter.IsFiltered(obj.GetType()))
                {
                    _retObject = obj;
                    _isSignaled = _event.Set();
                }
            }
            return TriggerReturnValue;
        }
    }
}
