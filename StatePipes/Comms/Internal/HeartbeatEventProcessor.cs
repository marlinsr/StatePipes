using StatePipes.Messages;

namespace StatePipes.Comms.Internal
{
    internal class HeartbeatEventProcessor
    {
        private readonly object _heartbeatLock = new();
        private DateTime _lastHearbeatTimestamp = DateTime.MinValue;
        private long _lastHearbeatCount = -1;
        private Timer? _timer;
#pragma warning disable CS8618 
        private event EventHandler Connected;
        private event EventHandler Disconnected;
        public void Subscribe(EventHandler onConnected, EventHandler onDisconnected)
        {
            lock (_heartbeatLock)
            {
                if (onConnected != null)
                {
                    Connected += onConnected;
                    if(IsConnectedToService) onConnected.Invoke(null,EventArgs.Empty);
                }
                if (onDisconnected != null) 
                {
                    Disconnected += onDisconnected;
                    if (!IsConnectedToService) onDisconnected.Invoke(null, EventArgs.Empty);
                }
            }
        }
        public void UnSubscribe(EventHandler onConnected, EventHandler onDisconnected)
        {
            lock (_heartbeatLock)
            {
                if (onConnected != null)
                {
                    Connected -= onConnected;
                }
                if (onDisconnected != null)
                {
                    Disconnected -= onDisconnected;
                }
            }
        }
        private bool _isConnectedToService;
        public bool IsConnectedToService
        {
            get
            {
                return _isConnectedToService;
            }
            private set
            {
                var prevValue = _isConnectedToService;
                _isConnectedToService = value;
                if (value != prevValue)
                {
                    if (value) Connected?.Invoke(null, EventArgs.Empty);
                    else Disconnected?.Invoke(null, EventArgs.Empty);
                }
            }
        }
        public void ResetHeartbeat()
        {
            lock (_heartbeatLock)
            {
                ResetHeartbeatWorker();
            }
        }
        private void ResetHeartbeatWorker()
        {
            _timer?.Dispose();
            _timer = null;
            _lastHearbeatTimestamp = DateTime.MinValue;
            _lastHearbeatCount = -1;
            IsConnectedToService = false;
        }
        public void HeartbeatEventHandler(HeartbeatEvent ev, BusConfig busConfig, bool isResponse)
        {
            try
            {
                lock (_heartbeatLock)
                {
                    _timer?.Dispose();
                    _timer = null;
                    if (ev.Counter > _lastHearbeatCount)
                    {
                        _lastHearbeatTimestamp = DateTime.UtcNow;
                        _lastHearbeatCount = ev.Counter;
                        IsConnectedToService = (DateTime.UtcNow - _lastHearbeatTimestamp).TotalMilliseconds < StatePipesConnectionFactory.HeartbeatIntervalMilliseconds * 3;
                        _timer = new Timer(HeartBeatTimeout, null, TimeSpan.FromMilliseconds(StatePipesConnectionFactory.HeartbeatIntervalMilliseconds * 3), TimeSpan.FromMilliseconds(Timeout.Infinite));
                    }
                    else
                    {
                        ResetHeartbeatWorker();
                    }
                }
            }
            catch
            {
                ResetHeartbeatWorker();
            }
        }
        private void HeartBeatTimeout(object? state)
        {
            ResetHeartbeat();
        }

    }
}
