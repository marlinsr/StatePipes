using Autofac;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Internal;
using StatePipes.StateMachine.Test.Internal;

namespace StatePipes.StateMachine.Test
{
    public class StateMachineForTest : IDisposable
    {
        private bool _disposed;
        public ContainerBuilder? Builder;
        public IContainer? Container;
        private TestStatePipesService? _testBus = new();
        private TestDependencyRegistration _testDependencyRegistration = new TestDependencyRegistration();
        private BaseStateMachine? _stateMachine;
        public void ConfigureStateMachine<StateMachineType, NextStateAfterInitType>(IDummyDependencyRegistration dummyRegisterator, bool disableAutomaticMoveToState = true) where StateMachineType : IStateMachine where NextStateAfterInitType : IStateMachineState
        {            
            var stateMachineType = typeof(StateMachineType);
            Builder = new ContainerBuilder();
            _testDependencyRegistration.DummyDependencyRegistration = dummyRegisterator;
            var dummyContainerSetup = new DummyContainerSetup<StateMachineType>(_testDependencyRegistration);
            var reflectedStateMachineContainerSetup = new StateMachineAndFirstStateContainerSetup<StateMachineType, NextStateAfterInitType>(disableAutomaticMoveToState);
            _testDependencyRegistration.Register(Builder);
            dummyContainerSetup.Register(Builder);
            reflectedStateMachineContainerSetup.Register(Builder);
            if(_testBus != null) Builder.RegisterInstance(_testBus).As<IStatePipesService>().SingleInstance();
            Container = Builder.Build();
            if (_testBus != null) _testBus.SetContainer(Container);
            dummyContainerSetup.Build(Container);
            reflectedStateMachineContainerSetup.Build(Container);
            _stateMachine = Container.Resolve<StateMachineManager>().GetStateMachineForType(stateMachineType);
        }
        public void ConfigureStateMachine<StateMachineType>(IDummyDependencyRegistration dummyRegisterator, bool disableAutomaticMoveToState = true) where StateMachineType : IStateMachine
        {
            var stateMachineType = typeof(StateMachineType);
            Builder = new ContainerBuilder();
            _testDependencyRegistration.DummyDependencyRegistration = dummyRegisterator;
            var dummyContainerSetup = new DummyContainerSetup<StateMachineType>(_testDependencyRegistration);
            var reflectedStateMachineContainerSetup = new StateMachineContainerSetup<StateMachineType>(disableAutomaticMoveToState);
            _testDependencyRegistration.Register(Builder);
            dummyContainerSetup.Register(Builder);
            reflectedStateMachineContainerSetup.Register(Builder);
            if (_testBus != null) Builder.RegisterInstance(_testBus).As<IStatePipesService>().SingleInstance();
            Container = Builder.Build();
            if (_testBus != null) _testBus.SetContainer(Container);
            dummyContainerSetup.Build(Container);
            reflectedStateMachineContainerSetup.Build(Container);
            _stateMachine = Container.Resolve<StateMachineManager>().GetStateMachineForType(stateMachineType);
        }
        public void Register<T>(object obj)
        {
            if (typeof(T).FullName == typeof(IStatePipesService).FullName) _testBus = null;
            _testDependencyRegistration.Register<T>(obj);
        }
        public void SendCommand<TCommand>(TCommand trigger) where TCommand : class, ICommand => _stateMachine?.SendCommand(trigger);
        public void PublishEvent<TEvent>(TEvent ev) where TEvent : class, IEvent => _stateMachine?.PublishEvent(ev);
        public void FilterCommand<T>(int skip = 0, int block = int.MaxValue) where T : class, ICommand => _testBus?.FilterCommand<T>(skip, block);  
        public bool RemoveCommandFilter<T>() where T : class, ICommand
        {
            if (_testBus == null) return false;
            return _testBus.RemoveCommandFilter<T>();
        }
        public void ClearCommandFilters() => _testBus?.ClearCommandFilters();
        public TimedBlockOnFilter<ICommand>? TrapCommand<T>(int skip, int timeoutMsec, bool stopProcessingCmdsWhenTriggered = false) where T : class, ICommand
        {
            if (_testBus == null) return null;
            return _testBus.TrapCommand<T>(skip, timeoutMsec, stopProcessingCmdsWhenTriggered);
        }
        public void RestartCommandBus()
        {
            if (_testBus == null) return;
            _testBus.Processing = true;
        }
        public List<ICommand> CommandInvocations
        {
            get => _testBus?.CommandInvocations ?? [];
        }
        public List<IEvent> EventInvocations
        {
            get => _testBus?.EventInvocations ?? [];
        }
        public TimedBlockOnFilter<IEvent>? TrapEvent<T>(int skip, int timeoutMsec) where T : class, IEvent
        {
            if (_testBus == null) return null;
            return _testBus.TrapEvent<T>(skip, timeoutMsec);
        }
        public void Start() => Fire(new InitTrigger());
        public void Fire(ITrigger trigger)
        {
            _stateMachine?.Fire(trigger);
            if (_stateMachine?.CurrentStateHasMoveToState() ?? false) FireMoveToStateTrigger();
        }
        public void FireMoveToStateTrigger() => _stateMachine?.Fire(new MoveToState());
        public bool IsCurrentState<T>() where T : IStateMachineState
        {
            if(_stateMachine == null) return false;
            return typeof(T).Name == _stateMachine.CurrentState;
        }
        public string CurrentState => _stateMachine?.CurrentState ?? string.Empty;
        #region IDisposable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            Container?.Dispose();
            _testBus?.Dispose();
            _disposed = true;
        }
        #endregion
    }
}
