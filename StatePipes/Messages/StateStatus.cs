namespace StatePipes.Messages
{
    public class StateStatus(string stateMachine, string state)
    {
        public string StateMachine { get; } = stateMachine;
        public string State { get; } = state;
    }
}
