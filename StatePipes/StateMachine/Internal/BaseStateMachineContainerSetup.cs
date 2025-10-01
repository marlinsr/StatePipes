using Autofac;
using Autofac.Util;
using StatePipes.Interfaces;

namespace StatePipes.StateMachine.Internal
{
    internal class BaseStateMachineContainerSetup : IContainerSetup
    {
        private readonly BaseStateMachineAndFirstStateContainerSetup _baseContainerSetup;

        public BaseStateMachineContainerSetup(Type stateMachineType, bool sendInitAfterInitialize = true, bool disableAutomaticMoveToState = false)
        {
            var firstStateType = typeof(IFirstStateForStateMachine);
            Type stateClassType = BaseStateMachineAndFirstStateContainerSetup.GetStateClassType(stateMachineType);

            var nextStateAfterInit = stateMachineType.Assembly.GetLoadableTypes().Where(t => firstStateType.IsAssignableFrom(t) && stateClassType.IsAssignableFrom(t)).FirstOrDefault();
            if (nextStateAfterInit == null) throw new InvalidOperationException($"FirstState for statemachine {stateMachineType.Name} not found! Use IFirstStateForStateMachine to designate first state.");
            _baseContainerSetup = new BaseStateMachineAndFirstStateContainerSetup(stateMachineType, nextStateAfterInit, sendInitAfterInitialize, disableAutomaticMoveToState);
        }

        public void Register(ContainerBuilder containerBuilder)
        {
            _baseContainerSetup.Register(containerBuilder);
        }

        public void Build(IContainer container)
        {
            _baseContainerSetup.Build(container);
        }
    }
}
