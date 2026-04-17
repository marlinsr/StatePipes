namespace StatePipes.ServiceCreatorTool
{
    internal class DllTypeSource(ReferencedAssemblies assemblies) : IServiceTypeSource
    {
        public TypeDescription? GetTypeDescription(string typeFullName)
            => assemblies.GetTypeDescription(typeFullName);

        public string? GetSimpleName(string typeFullName)
        {
            var t = assemblies.GetTypeOf(typeFullName);
            return t?.Name;
        }
    }
}
