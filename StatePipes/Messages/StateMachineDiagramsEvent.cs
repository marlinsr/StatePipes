using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    public class StateMachineDiagramsEvent(IReadOnlyList<string> diagrams) : IEvent
    {
        public IReadOnlyList<string> Diagrams { get; } = diagrams;
    }
}
