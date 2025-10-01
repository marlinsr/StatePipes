namespace StatePipes.StateMachine.Test.Internal
{
    internal class SendFilterList<A>
    {
        private readonly object _lock = new();
        private readonly Dictionary<Type, BaseFilter<A>> _sendFilters = [];
        private DefaultFilter<A> _defaultSendFilter = new();
        public SendFilterList()
        {
            _sendFilters[typeof(DefaultFilter<A>)] = _defaultSendFilter;
        }
        public DefaultFilter<A>? DefaultFilter => (DefaultFilter<A>?)Get<DefaultFilter<A>>();
        public void FilterCommand<T>(int skip = 0, int block = int.MaxValue) where T : class, A
        {
            lock (_lock)
            {
                _defaultSendFilter.Add<T>(skip, block);
            }
        }
        public bool RemoveCommandFilter<T>() where T : class, A
        {
            lock (_lock)
            {
                return _defaultSendFilter.Remove<T>();
            }
        }
        public void ClearCommandFilters()
        {
            lock (_lock)
            {
                _defaultSendFilter.Clear();
            }
        }
        public void Add<T>(T filter) where T : BaseFilter<A>
        {
            if (typeof(T).FullName == typeof(DefaultFilter<A>).FullName) throw new ArgumentException("Cannot add a DefaultSendFilter");
            lock (_lock)
            {
                _sendFilters.Add(typeof(T), (T)filter.Clone());
            }
        }
        public BaseFilter<A>? Get<T>() where T : BaseFilter<A>
        {
            lock (_lock)
            {
                if (_sendFilters.TryGetValue(typeof(T), out BaseFilter<A>? filter)) return (T?)filter?.Clone();
                return null;
            }
        }
        public bool Remove<T>() where T : BaseFilter<A>
        {
            lock (_lock)
            {
                return _sendFilters.Remove(typeof(T));
            }
        }
        public bool IsFiltered<T>() where T : A => IsFiltered(typeof(T));
        public bool IsFiltered(Type commandType)
        {
            lock (_lock)
            {
                bool ret = false;
                foreach (var filter in _sendFilters.Values)
                {
                    ret |= filter.IsFiltered(commandType);
                }
                return ret;
            }
        }
        public void Clear()
        {
            lock (_lock)
            {
                _sendFilters.Clear();
                _defaultSendFilter.Clear();
                _sendFilters.Add(typeof(DefaultFilter<A>), _defaultSendFilter);
            }
        }
    }
}
