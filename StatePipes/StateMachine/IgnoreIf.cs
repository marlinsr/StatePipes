namespace StatePipes.StateMachine
{
    [AttributeUsage(AttributeTargets.Method)]
    public class IgnoreIf(string? guardDescription = null) : Attribute
    {
        public string? GuardDescription { get; } = guardDescription;
    }
}
