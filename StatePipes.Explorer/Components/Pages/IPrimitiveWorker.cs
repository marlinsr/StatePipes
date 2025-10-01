namespace StatePipes.Explorer.Components.Pages
{
    public interface IPrimitiveWorker
    {
        string? GetValueFromString(string? str, bool nullable);
        string? DefaultValue();
    }
}
