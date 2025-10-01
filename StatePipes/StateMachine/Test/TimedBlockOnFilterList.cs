namespace StatePipes.StateMachine.Test
{
    internal class TimedBlockOnFilterList<A> : IDisposable
    {
        private readonly List<TimedBlockOnFilter<A>> _filterList = new List<TimedBlockOnFilter<A>>();
        private readonly object _lock = new object();
        private bool _disposed;
        public void Add(TimedBlockOnFilter<A> filter)
        {
            if (_disposed) return;
            lock (_lock)
            {
                _filterList.Add(filter);
            }
        }
        public void Dispose()
        {
            if (_disposed) return;
            GC.SuppressFinalize(this);
            _disposed = true;
            lock (_lock)
            {
                _filterList.ForEach(x => x.Dispose());
                _filterList.Clear();
            }
        }
        public bool Trigger<T>(T obj) where T : A
        {
            if (_disposed) return false;
            lock (_lock)
            {
                bool ret = false;
                foreach (var filter in _filterList)
                {
                    ret |= filter.Trigger(obj);
                }
                return ret;
            }
        }
        public void RemoveTriggerReturnValueTrueItems()
        {
            if (_disposed) return;
            lock (_lock)
            {
                _filterList.Where(t => t.TriggerReturnValue).ToList().ForEach(r => _filterList.Remove(r));
            }
        }
    }
}
