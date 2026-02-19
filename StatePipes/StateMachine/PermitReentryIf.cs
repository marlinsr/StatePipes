namespace StatePipes.StateMachine
{
    [AttributeUsage(AttributeTargets.Method)]
    public class PermitReentryIf(string? guardDescription = null) : Attribute
    {
            public string? GuardDescription { get; } = guardDescription;
    }
}
