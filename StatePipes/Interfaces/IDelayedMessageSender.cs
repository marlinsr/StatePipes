namespace StatePipes.Interfaces
{
    public interface IDelayedMessageSender<TMessage> where TMessage : class, IMessage
    {
        public bool Enabled { get; }
        void StartOneShot(TimeSpan dueTime, TMessage message);
        void StartPeriodic(TimeSpan period, TMessage message);
        void Stop();
    }
}
