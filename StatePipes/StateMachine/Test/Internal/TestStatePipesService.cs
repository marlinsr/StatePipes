using Autofac;
using StatePipes.Common;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using System.Diagnostics;
using System.Reflection;
namespace StatePipes.StateMachine.Test.Internal
{
    internal class TestStatePipesService : TaskWrapper<ReceivedCommandMessage>, IStatePipesService
    {
        private IContainer? _container = null;
        private readonly object _cmdLock = new();
        private readonly List<ICommand> _cmdInvocations = [];
        private readonly TimedBlockOnFilterList<ICommand> _cmdBlockingFilterList = new();
        private readonly SendFilterList<ICommand> _cmdFilterList = new SendFilterList<ICommand>();
        private readonly TimedBlockOnFilterList<IEvent> _eventBlockingFilterList = new TimedBlockOnFilterList<IEvent>();
        private readonly object _eventLock = new();
        private readonly List<IEvent> _eventInvocations = [];
        public TimedBlockOnFilter<ICommand>? TrapCommand<T>(int skip, int timeoutMsec, bool stopProcessingCmdsWhenTriggered) where T : class, ICommand
        {
            if (_disposed) return null;
            var filter = new DefaultFilter<ICommand>();
            filter.Add<T>(skip, int.MaxValue);
            var timedBlocking = new TimedBlockOnFilter<ICommand>(filter, timeoutMsec, stopProcessingCmdsWhenTriggered);
            _cmdBlockingFilterList.Add(timedBlocking);
            return timedBlocking;
        }
        public TimedBlockOnFilter<IEvent> TrapEvent<T>(int skip, int timeoutMsec) where T : IEvent
        {
            var filter = new DefaultFilter<IEvent>();
            filter.Add<T>(skip, int.MaxValue);
            var timedBlocking = new TimedBlockOnFilter<IEvent>(filter, timeoutMsec);
            _eventBlockingFilterList.Add(timedBlocking);
            return timedBlocking;
        }
        private bool _processing = true;
        public bool Processing
        {
            get => _processing;
            set
            {
                if (_disposed || _processing == value) return;
                if (value) _cmdBlockingFilterList.RemoveTriggerReturnValueTrueItems();
                _processing = value;
            }
        }
        public void SetContainer(IContainer container)
        {
            _container = container;
            Start();
        }
        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            base.Dispose();
            _cmdBlockingFilterList.Dispose();
            _eventBlockingFilterList.Dispose();
        }
        private void AddCommandToInvocations(ICommand command)
        {
            lock (_cmdLock)
            {
                _cmdInvocations.Add(command);
            }
        }
        public List<ICommand> CommandInvocations
        {
            get
            {
                if (_disposed) return [];
                lock (_cmdLock)
                {
                    List<ICommand> ret = [];
                    _cmdInvocations.ForEach(command => ret.Add(command));
                    return ret;
                }
            }
        }
        public List<IEvent> EventInvocations
        {
            get
            {
                if (_disposed) return [];
                lock (_eventLock)
                {
                    List<IEvent> ret = [];
                    _eventInvocations.ForEach(ev => ret.Add(ev));
                    return ret;
                }
            }
        }
        private void Execute(ReceivedCommandMessage cmd)
        {
            try
            {
                var commandType = cmd.Command.GetType();
                if (commandType == null) return;
                var commandHandlerType = typeof(IMessageHandler<>).MakeGenericType(commandType);
                if (commandHandlerType == null) return;
                var cmdHandler = _container?.Resolve(commandHandlerType);
                if (cmdHandler == null) return;
                var executeMethod = commandHandlerType.GetMethod("HandleMessage", BindingFlags.Public | BindingFlags.Instance);
                if (executeMethod == null) return;
                AddCommandToInvocations((ICommand)cmd.Command);
                executeMethod.Invoke(cmdHandler, new object[] { cmd.Command, cmd.ReplyTo, false });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
        protected override void DoWork()
        {
            while (true)
            {
                PerformCancellation();
                if (_container != null && _processing)
                {
                    var cmd = WaitGetNext();
                    PerformCancellation();
                    if (cmd != null)
                    {
                        Execute(cmd);
                        _processing = !_cmdBlockingFilterList.Trigger((ICommand)cmd.Command);
                    }
                }
                else
                {
                    PerformCancellation();
                    _ = Thread.Yield();
                }
            }
        }
        public void Add<T>(T filter) where T : BaseFilter<ICommand> => _cmdFilterList.Add(filter);
        public bool Remove<T>() where T : BaseFilter<ICommand> => _cmdFilterList.Remove<T>();
        public void FilterCommand<T>(int skip = 0, int block = int.MaxValue) where T : class, ICommand => _cmdFilterList.FilterCommand<T>(skip, block);
        public bool RemoveCommandFilter<T>() where T : class, ICommand => _cmdFilterList.RemoveCommandFilter<T>();
        public void ClearCommandFilters() => _cmdFilterList.ClearCommandFilters();
        public bool IsConnectedToBroker { get; private set; }
        public void PublishEvent<TEvent>(TEvent eventMessage) where TEvent : class, IEvent
        {
            _eventBlockingFilterList.Trigger(eventMessage);
            lock (_eventLock) { _eventInvocations.Add(eventMessage); }
        }
        public void SendCommand<TCommand>(TCommand command) where TCommand : class, ICommand => SendCommand(command, null);
        public void SendCommand<TCommand>(TCommand command, BusConfig? busConfig) where TCommand : class, ICommand
        {
            if (_cmdFilterList.IsFiltered(command.GetType())) return;
            Queue(new ReceivedCommandMessage(command, busConfig == null ? new BusConfig(string.Empty, string.Empty, string.Empty, string.Empty) : busConfig));
        }
        public void SendMessage<TMessage>(TMessage message) where TMessage : class, IMessage
        {
            if (message is ICommand command) SendCommand(command);
            if (message is IEvent ev) PublishEvent(ev);
        }
        public void SendResponse<TEvent>(TEvent replyMessage, BusConfig busConfig) where TEvent : class, IEvent => PublishEvent(replyMessage);
        public void Start()
        {
            StartAndWait();
            IsConnectedToBroker = true;
        }
        public void Stop()
        {
            Cancel();
            IsConnectedToBroker = false;
        }
    }
}
