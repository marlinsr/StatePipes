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
            foreach (var sm in stateMachines)
            {
                var dotString = sm.GetDotGraph();
                var lastBracketIndex = dotString.LastIndexOf('}');
                if (lastBracketIndex > 0)
                {
                    var modifiedDotString = dotString.Substring(0, lastBracketIndex);
                    modifiedDotString += $"\"{sm.CurrentState}\" [style=\"filled\" fillcolor = \"yellow\"];\r\n";
                    modifiedDotString += dotString.Substring(lastBracketIndex);
                    dotString = modifiedDotString;
                }
                diagrams.Add(dotString);
            }
            stateMachines.First().SendResponse(new StateMachineDiagramsEvent(diagrams), responseInfo);
        }
    }
}
