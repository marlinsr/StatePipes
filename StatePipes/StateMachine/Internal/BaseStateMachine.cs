using Stateless;
using Stateless.Graph;
using StatePipes.Common.Internal;
using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.Messages;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.StateMachine.Internal
{
    internal class BaseStateMachine
    {
        private readonly StateMachine<string, string> _stateMachine;
        private readonly IStatePipesService _bus;
        private StateMachineManager? _stateMachineManager;
        private readonly InitialState _initialState = new();
        private dynamic? _initTriggerCommand;
        private string _currentState;
        private CurrentTrigger? _currentTrigger;
        private string _moveToStateName = typeof(MoveToState).Name;
        public string StateMachineName { get; private set; } = "UNKNOWN";
        public string AssemblyQualifiedStateMachineName { get; private set; } = "UNKNOWN";
        public BaseStateMachine(IStatePipesService bus)
        {
            _bus = bus;
            _currentState = typeof(InitialState).Name;
            _stateMachine = new StateMachine<string, string>(typeof(InitialState).Name, FiringMode.Immediate);
            _stateMachine.OnUnhandledTrigger(OnUnhandledTrigger);
            _stateMachine.OnTransitioned(t =>
            {
                Log?.LogVerbose($"[State Changing] {t.Trigger}: {t.Source} --> {t.Destination}");
                _currentState = t.Destination;
                SendCurrentStatus();
            });
        }
        public void OnUnhandledTrigger(string state, string trigger) => Log?.LogVerbose($"Trigger not set: [{trigger}] on state [{state}] on state machine [{StateMachineName}]");
        internal void SetStateMachineManagerAndType(StateMachineManager stateMachineManager, Type stateMachineType)
        {
            _stateMachineManager = stateMachineManager;
            StateMachineName = stateMachineType.FullName ?? "UNKNOWN";
            AssemblyQualifiedStateMachineName = stateMachineType.AssemblyQualifiedName ?? "UNKNOWN";
        }
        public void SendInit()
        {
            if (_initTriggerCommand == null) return;
            SendCommand(_initTriggerCommand);
        }
        public void ConfigureInitialState(Type nextStateAfterInitType, dynamic initTriggerCommand)
        {
            _initTriggerCommand = initTriggerCommand;
            _stateMachine.Configure(_initialState.GetType().Name).Permit(typeof(InitTrigger).Name, nextStateAfterInitType.Name);
        }
        public CurrentTrigger? CurrentTrigger => _currentTrigger;
        public TTrigger? GetCurrentTrigger<TTrigger>() where TTrigger : ITrigger
        {
            if (_currentTrigger == null || _currentTrigger.Trigger is not TTrigger) return default;
            return (TTrigger)_currentTrigger.Trigger;
        }
        public BusConfig? GetCurrentResponseInfo()
        {
            if(_currentTrigger is null) return null;
            return _currentTrigger.ResponseInfo;
        }
        public string CurrentState =>_currentState; 
        public StateMachine<string, string>.StateConfiguration Configure(string stateName) => _stateMachine.Configure(stateName);
        public bool Fire<TTrigger>(TTrigger trigger, BusConfig? responseInfo = null) where TTrigger : ITrigger
        {
            var triggerTypeName = trigger.GetType().Name;
            if(triggerTypeName != _moveToStateName) _currentTrigger = new CurrentTrigger(trigger, responseInfo);
            _stateMachine.Fire(triggerTypeName);
            return true;
        }
        public bool FireExternal<TStateMachine, BaseTriggerCommandType>(BaseTriggerCommandType trigger, BusConfig? responseInfo = null) 
            where TStateMachine : IStateMachine where BaseTriggerCommandType : BaseTriggerCommand<TStateMachine>
        {
            return _stateMachineManager?.GetStateMachine<TStateMachine>()?.Fire(trigger, responseInfo) ?? false;
        }
        public void SendCommand<TCommand>(TCommand trigger, BusConfig? responseInfo = null) where TCommand : class, ICommand => 
            _bus.SendCommand(trigger, responseInfo);
        public void PublishEvent<TEvent>(TEvent ev) where TEvent : class, IEvent => _bus.PublishEvent(ev);
        public void SendResponse<TEvent>(TEvent ev, BusConfig responseInfo) where TEvent : class, IEvent => _bus.SendResponse(ev, responseInfo);
        public IDelayedMessageSender<TMessage> CreateDelayedMessageSender<TMessage>() where TMessage : class, IMessage
        {
            return new DelayedMessageSender<TMessage>(_bus);
        }
        public void SendCurrentStatus(BusConfig? responseInfo = null)
        {
            if (responseInfo == null)
                _bus.PublishEvent(new StateStatusEvent(CurrentState, StateMachineName));
            else
                _bus.SendResponse(new StateStatusEvent(CurrentState, StateMachineName), responseInfo);
        }
        public void SendCurrentStatusAllStateMachines() => _stateMachineManager?.GetAllStateMachines().ForEach(stateMachine => stateMachine.SendCurrentStatus());
        public string GetDotGraph()
        {
            var info = _stateMachine.GetInfo();
            return UmlDotGraph.Format(info);
        }
        private string SaveDotGraph(string dotFilename)
        {
            string graph = GetDotGraph();
            File.WriteAllText(dotFilename, graph);
            return dotFilename;
        }
        public virtual string SaveDotGraphToPath(string path)=> SaveDotGraph($@"{path}\{StateMachineName}.dot");
        public bool CurrentStateHasMoveToState()
        {
            var info = _stateMachine.GetInfo();
            bool hasMoveToState = info.States.FirstOrDefault(s => (string)s.UnderlyingState == CurrentState)?.FixedTransitions.FirstOrDefault(t => (string)t.Trigger.UnderlyingTrigger == _moveToStateName) != null;
            return hasMoveToState;
        }
    }
}
