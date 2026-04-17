namespace StatePipes.ServiceCreatorTool
{
    internal class LiveTypeSource(TypeSerializationList typeSerializationList) : IServiceTypeSource
    {
        public TypeDescription? GetTypeDescription(string typeFullName)
        {
            foreach (var ts in typeSerializationList.TypeSerializations)
            {
                if (ts.HasTypeDescription(typeFullName))
                    return ts.GetTypeDescription(typeFullName);
            }
            return null;
        }

        // Return null to fall through to string-based name derivation in ProxyGeneratorCommon.GetTypeName
        public string? GetSimpleName(string typeFullName) => null;
    }
}
