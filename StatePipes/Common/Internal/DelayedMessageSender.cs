using StatePipes.Interfaces;

namespace StatePipes.Common.Internal
{
    internal class DelayedMessageSender<TMessage> : IDelayedMessageSender<TMessage> where TMessage : class, IMessage
    {
        private readonly IMessageSender _bus;
        private readonly object _lock = new object();
        private Timer? _timer;
        private bool _disposed;
        private bool _isPeriodic;
        public bool Enabled
        {
            get;
            private set;
        }
        public DelayedMessageSender(IMessageSender bus)
        {
            _bus = bus;
        }
        public void Dispose()
        {
            Dispose(true);
        }
        public void StartOneShot(TimeSpan dueTime, TMessage message)
        {
            Stop();
            lock (_lock)
            {
                _timer = new Timer(SendMessage, message, dueTime, TimeSpan.FromMilliseconds(Timeout.Infinite));
                Enabled = true;
            }
        }
        public void StartPeriodic(TimeSpan period, TMessage message)
        {
            Stop();
            lock (_lock)
            {
                Enabled = true;
                _isPeriodic = true;
                _timer = new Timer(SendMessage, message, period, period);
            }
        }
        public void Stop()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
                Enabled = false;
                _isPeriodic = false;
            }
        }
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing) Stop();
            _disposed = true;
        }
        private void SendMessage(object? state)
        {
            lock (_lock)
            {
                var message = state as TMessage;
                if (message != null) _bus.SendMessage(message);
                if(!_isPeriodic) Enabled = false;
            }
        }
    }
}
