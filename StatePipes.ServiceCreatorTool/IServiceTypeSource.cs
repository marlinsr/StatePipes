namespace StatePipes.ServiceCreatorTool
{
    internal interface IServiceTypeSource
    {
        TypeDescription? GetTypeDescription(string typeFullName);
        string? GetSimpleName(string typeFullName);
    }
}
