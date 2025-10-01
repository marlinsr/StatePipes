namespace StatePipes.Interfaces
{
    public interface IMessageSender
    {
        void SendMessage<TMessage>(TMessage message) where TMessage : class, IMessage;
    }
}
