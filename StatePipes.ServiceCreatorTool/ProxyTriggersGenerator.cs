namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyTriggersGenerator
    {
        private readonly ProxyGeneratorCommon _proxyGeneratorCommon;
        public ProxyTriggersGenerator(ProxyGeneratorCommon proxyGeneratorCommon)
        {
            _proxyGeneratorCommon = proxyGeneratorCommon;
        }
        public void CreateTriggersStart()
        {
            _proxyGeneratorCommon.CodeGenerationString.ResetIndention();
            var nmspc = $"{_proxyGeneratorCommon.stateMachineNamespace}.Triggers.Internal";
            _proxyGeneratorCommon.NamespaceList.AddNamespace(nmspc);
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"namespace {nmspc}");
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using {_proxyGeneratorCommon.CodeGenerationNamespace};");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using Newtonsoft.Json;");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using System.Diagnostics;");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using StatePipes.StateMachine;");

            CreateTriggers();
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private void CreateTriggers()
        {
            foreach (TypeSerialization typeSerialization in _proxyGeneratorCommon.TypeSerializations.TypeSerializations)
            {
                var typeDescription = typeSerialization.GetTopLevelTypeDescription();
                if (typeDescription.IsEvent)
                {
                    _proxyGeneratorCommon.NamespaceList.AddNamespace(typeDescription.Namespace);
                    CreateTrigger(typeDescription.FullName);
                }
            }
        }
        public string CreateTriggerTypeName(string typeFullName) => $"{_proxyGeneratorCommon.GetTypeName(typeFullName)}From{_proxyGeneratorCommon.ProxyMoniker}Trigger";
        public string CreateValueObjectTypeNameForEvent(string typeFullName) => $"{_proxyGeneratorCommon.GetTypeName(typeFullName)}From{_proxyGeneratorCommon.ProxyMoniker}";
        private void CreateTrigger(string typeFullName)
        {
            var valueObjectName = CreateValueObjectTypeNameForEvent(typeFullName);
            var triggerName = CreateTriggerTypeName(typeFullName);
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"internal class {triggerName}: BaseTriggerCommand<{_proxyGeneratorCommon.StateMachineFullName}>");
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public string ProxyName {{ get; }}");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {valueObjectName} Data {{ get; }}");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"[JsonConstructor]");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {triggerName}(string proxyName, {valueObjectName} data)");
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"ProxyName = proxyName;");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"Data = data;");
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
            _proxyGeneratorCommon.CreateHelperContructor(triggerName, valueObjectName);
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
    }
}
