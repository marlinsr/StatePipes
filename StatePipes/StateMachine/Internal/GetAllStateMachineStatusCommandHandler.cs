using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.StateMachine.Internal
{
    internal class GetAllStateMachineStatusCommandHandler(IStatePipesService _bus, StateMachineManager _stateMachineManager) : IMessageHandler<GetAllStateMachineStatusCommand>
    {
        public void HandleMessage(GetAllStateMachineStatusCommand command, BusConfig? responseInfo, bool isResponse)
        {
            if (responseInfo == null) return;
            List<StateStatus> stateStatuses = [];
            _stateMachineManager.GetAllStateMachines().ForEach(sm =>
            {
                stateStatuses.Add(new StateStatus(sm.StateMachineName, sm.CurrentState));
                sm.SendCurrentStatus(responseInfo);
            });
            _bus.SendResponse(new AllStateStatusEvent(stateStatuses), responseInfo);
        }
    }
}
