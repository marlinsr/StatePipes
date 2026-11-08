namespace StatePipes.Interfaces
{
    public interface IDelayedMessageSender<TMessage> where TMessage : class, IMessage
    {
        public Guid TimerId { get; }
        public bool Enabled { get; }
        void StartOneShot(TimeSpan dueTime, TMessage message);
        void StartPeriodic(TimeSpan period, TMessage message);
        void Stop();
    }
}
