namespace StatePipes.StateMachine
{
    [AttributeUsage(AttributeTargets.Method)]
    public class PermitIf(Type destinationState, string? guardDescription = null) : Attribute
    {
        public Type DestinationState { get; } = destinationState;
        public string? GuardDescription { get; } = guardDescription;
    }
}
