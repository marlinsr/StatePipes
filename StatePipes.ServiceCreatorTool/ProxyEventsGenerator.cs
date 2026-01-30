namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyEventsGenerator(ProxyGeneratorCommon proxyGeneratorCommon)
    {
        public void CreateEventsStart()
        {
            proxyGeneratorCommon.CodeGenerationString.ResetIndention();
            var nmspc = $"{proxyGeneratorCommon.CodeGenerationBaseNamespace}.Events.Internal";
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"namespace {nmspc}");
            proxyGeneratorCommon.CodeGenerationString.Indent();
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using {proxyGeneratorCommon.CodeGenerationBaseNamespace}.ValueObjects.{proxyGeneratorCommon.ProxyMoniker};");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using Newtonsoft.Json;");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using System.Diagnostics;");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using StatePipes.Interfaces;");
            CreateEvents();
            proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private void CreateEvents()
        {
            foreach (TypeSerialization typeSerialization in proxyGeneratorCommon.TypeSerializations.TypeSerializations)
            {
                var typeDescription = typeSerialization.GetTopLevelTypeDescription();
                if (typeDescription.IsCommand)
                {
                    CreateEvent(typeDescription.FullName);
                }
            }
        }
        private void CreateEvent(string fullName)
        {
            var valueObjectName = CreateValueObjectTypeNameForCommand(fullName);
            var eventName = CreateEventTypeName(fullName);
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"internal class {eventName}: IEvent");
            proxyGeneratorCommon.CodeGenerationString.Indent();
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public string ProxyName {{ get; }}");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {valueObjectName} Data {{ get; }}");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"[JsonConstructor]");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {eventName}(string proxyName, {valueObjectName} data)");
            proxyGeneratorCommon.CodeGenerationString.Indent();
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"ProxyName = proxyName;");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"Data = data;");
            proxyGeneratorCommon.CodeGenerationString.Outdent();
            proxyGeneratorCommon.CreateHelperContructor(eventName, valueObjectName);
            proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        public string CreateEventTypeName(string typeFullName) => $"{proxyGeneratorCommon.GetTypeName(typeFullName)}To{proxyGeneratorCommon.ProxyMoniker}Event";
        private string CreateValueObjectTypeNameForCommand(string typeFullName) => $"{proxyGeneratorCommon.GetTypeName(typeFullName)}To{proxyGeneratorCommon.ProxyMoniker}";
    }
}
