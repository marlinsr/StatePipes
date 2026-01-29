namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyTriggersGenerator(ProxyGeneratorCommon proxyGeneratorCommon)
    {
        public void CreateTriggersStart()
        {
            proxyGeneratorCommon.CodeGenerationString.ResetIndention();
            var nmspc = $"{proxyGeneratorCommon.StateMachineNamespace}.Triggers.Internal";
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"namespace {nmspc}");
            proxyGeneratorCommon.CodeGenerationString.Indent();
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using {proxyGeneratorCommon.CodeGenerationBaseNamespace}.ValueObjects.{proxyGeneratorCommon.ProxyMoniker};");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using Newtonsoft.Json;");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using System.Diagnostics;");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using StatePipes.StateMachine;");

            CreateTriggers();
            proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private void CreateTriggers()
        {
            foreach (TypeSerialization typeSerialization in proxyGeneratorCommon.TypeSerializations.TypeSerializations)
            {
                var typeDescription = typeSerialization.GetTopLevelTypeDescription();
                if (typeDescription.IsEvent)
                {
                    proxyGeneratorCommon.NamespaceList.AddNamespace(typeDescription.Namespace);
                    CreateTrigger(typeDescription.FullName);
                }
            }
        }
        public string CreateTriggerTypeName(string typeFullName) => $"{proxyGeneratorCommon.GetTypeName(typeFullName)}From{proxyGeneratorCommon.ProxyMoniker}Trigger";
        public string CreateValueObjectTypeNameForEvent(string typeFullName) => $"{proxyGeneratorCommon.GetTypeName(typeFullName)}From{proxyGeneratorCommon.ProxyMoniker}";
        private void CreateTrigger(string typeFullName)
        {
            var valueObjectName = CreateValueObjectTypeNameForEvent(typeFullName);
            var triggerName = CreateTriggerTypeName(typeFullName);
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"internal class {triggerName}: BaseTriggerCommand<{proxyGeneratorCommon.StateMachineFullName}>");
            proxyGeneratorCommon.CodeGenerationString.Indent();
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public string ProxyName {{ get; }}");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {valueObjectName} Data {{ get; }}");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"[JsonConstructor]");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {triggerName}(string proxyName, {valueObjectName} data)");
            proxyGeneratorCommon.CodeGenerationString.Indent();
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"ProxyName = proxyName;");
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"Data = data;");
            proxyGeneratorCommon.CodeGenerationString.Outdent();
            proxyGeneratorCommon.CreateHelperContructor(triggerName, valueObjectName);
            proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
    }
}
