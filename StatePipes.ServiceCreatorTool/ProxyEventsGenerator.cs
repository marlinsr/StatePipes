namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyEventsGenerator
    {
        private readonly ProxyGeneratorCommon _proxyGeneratorCommon;
        public ProxyEventsGenerator(ProxyGeneratorCommon proxyGeneratorCommon)
        {
            _proxyGeneratorCommon = proxyGeneratorCommon;
        }
        public void CreateEventsStart()
        {
            _proxyGeneratorCommon.CodeGenerationString.ResetIndention();
            var nmspc = $"{_proxyGeneratorCommon.CodeGenerationBaseNamespace}.Events.Internal";
            _proxyGeneratorCommon.NamespaceList.AddNamespace(nmspc);
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"namespace {nmspc}");
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using {_proxyGeneratorCommon.CodeGenerationNamespace};");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using Newtonsoft.Json;");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using System.Diagnostics;");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using StatePipes.Interfaces;");
            CreateEvents();
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private void CreateEvents()
        {
            foreach (TypeSerialization typeSerialization in _proxyGeneratorCommon.TypeSerializations.TypeSerializations)
            {
                var typeDescription = typeSerialization.GetTopLevelTypeDescription();
                if (typeDescription.IsCommand)
                {
                    _proxyGeneratorCommon.NamespaceList.AddNamespace(typeDescription.Namespace);
                    CreateEvent(typeDescription.FullName);
                }
            }
        }
        private void CreateEvent(string fullName)
        {
            var valueObjectName = CreateValueObjectTypeNameForCommand(fullName);
            var eventName = CreateEventTypeName(fullName);
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"internal class {eventName}: IEvent");
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public string ProxyName {{ get; }}");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {valueObjectName} Data {{ get; }}");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"[JsonConstructor]");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {eventName}(string proxyName, {valueObjectName} data)");
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"ProxyName = proxyName;");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"Data = data;");
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
            _proxyGeneratorCommon.CreateHelperContructor(eventName, valueObjectName);
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        public string CreateEventTypeName(string typeFullName) => $"{_proxyGeneratorCommon.GetTypeName(typeFullName)}To{_proxyGeneratorCommon.ProxyMoniker}Event";
        private string CreateValueObjectTypeNameForCommand(string typeFullName) => $"{_proxyGeneratorCommon.GetTypeName(typeFullName)}To{_proxyGeneratorCommon.ProxyMoniker}";
    }
}
