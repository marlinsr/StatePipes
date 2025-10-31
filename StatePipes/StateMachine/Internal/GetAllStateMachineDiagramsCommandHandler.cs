using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.StateMachine.Internal
{
    internal class GetAllStateMachineDiagramsCommandHandler(StateMachineManager stateMachineManger) : IMessageHandler<GetAllStateMachineDiagramsCommand>
    {
        public void HandleMessage(GetAllStateMachineDiagramsCommand command, BusConfig? responseInfo, bool isResponse)
        {
            if (responseInfo == null) return;
            var stateMachines = stateMachineManger.GetAllStateMachines();
            if (!stateMachines.Any()) return;
            List<string> diagrams = new List<string>();
            stateMachines.ForEach(sm => diagrams.Add(sm.GetDotGraph()));
            stateMachines.First().SendResponse(new StateMachineDiagramsEvent(diagrams), responseInfo);
        }
    }
}
