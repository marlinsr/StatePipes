namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyGenerator
    {
        private readonly ProxyGeneratorCommon _proxyGeneratorCommon;
        private readonly ProxyValueObjectsGenerator _proxyValueObjectsGenerator;
        private readonly ProxyTriggersGenerator _proxyTriggersGenerator;
        private readonly ProxyEventsGenerator _proxyEventsGenerator;
        public string DefaultConfigNamespace => _proxyGeneratorCommon.DefaultConfigNamespace;
        public ProxyGenerator(string fullPathFileName, string codeGenerationBaseNamespace, string proxyMoniker, PathHelper pathProvider)
        {
            _proxyGeneratorCommon = new(fullPathFileName, codeGenerationBaseNamespace, proxyMoniker, pathProvider);
            _proxyValueObjectsGenerator = new(_proxyGeneratorCommon);
            _proxyTriggersGenerator = new(_proxyGeneratorCommon);
            _proxyEventsGenerator = new(_proxyGeneratorCommon);
            GenerateCode();
        }
        private void GenerateCode()
        {
            TypeSerializationConverter typeSerializationConverter = new TypeSerializationConverter();
            _proxyGeneratorCommon.Types.Add(_proxyGeneratorCommon.Assemblies.AllStateStatusEventType!);
            _proxyGeneratorCommon.Types.Add(_proxyGeneratorCommon.Assemblies.StateStatusEventType!);
            _proxyGeneratorCommon.Types.Add(_proxyGeneratorCommon.Assemblies.GetAllStateMachineStatusCommandType!);
            _proxyGeneratorCommon.Types.ForEach(t => _proxyGeneratorCommon.TypeSerializations.TypeSerializations.Add(typeSerializationConverter.CreateFromType(t, _proxyGeneratorCommon.Assemblies.CommandType!, _proxyGeneratorCommon.Assemblies.EventType!)));
            _proxyGeneratorCommon.NamespaceList.AddNamespace("StatePipes.Comms");
            _proxyGeneratorCommon.NamespaceList.AddNamespace("StatePipes.Common");
            _proxyValueObjectsGenerator.CreateValueObjectsStart();
            _proxyTriggersGenerator.CreateTriggersStart();
            _proxyEventsGenerator.CreateEventsStart();
            CreateProxyStart();
        }
        private string GetGlobalUsing()
        {
            string usingString = string.Empty;
            foreach (var nmspc in _proxyGeneratorCommon.NamespaceList)
            {
                usingString += $"using {nmspc};\r";
            }
            return usingString;
        }
        public void SaveToFile(string fileName) => File.WriteAllText(fileName, GetGlobalUsing() + GetString());
        private string GetString() => _proxyGeneratorCommon.CodeGenerationString.ToString();
        private void CreateProxyStart()
        {
            _proxyGeneratorCommon.CodeGenerationString.ResetIndention();
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"namespace {_proxyGeneratorCommon.CodeGenerationBaseNamespace}.Proxies");
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine("");
            CreateProxy();
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private void CreateProxy()
        {
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"using StatePipes.Interfaces;");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"internal class {_proxyGeneratorCommon.ProxyMoniker}Proxy : BaseGeneratedProxy");
            CreateEventHandlerDeclarations();
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {_proxyGeneratorCommon.ProxyMoniker}Proxy(IStatePipesProxyFactory proxyFactory, IStatePipesService bus) : base(proxyFactory, bus) {{ }}");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public override string ProxyPrefix => \"{_proxyGeneratorCommon.ProxyMoniker}\";");
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"protected override void SendConnectionStatusTrigger(string proxyName, bool isConnected) => _bus.SendCommand(new ProxyConnectionStatusTrigger(proxyName, isConnected));");
            CreateHandleMessageMethods();
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"protected override void Subscribe(IStatePipesProxy proxy)");
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            CreateSubscriptions();
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private void CreateSubscriptions()
        {
            foreach (TypeSerialization typeSerialization in _proxyGeneratorCommon.TypeSerializations.TypeSerializations)
            {
                var typeDescription = typeSerialization.GetTopLevelTypeDescription();
                if (typeDescription.IsEvent)
                {
                    var eventTypeName = _proxyGeneratorCommon.RemoveJunk(typeDescription.FullName);
                    var triggerName = _proxyTriggersGenerator.CreateTriggerTypeName(typeDescription.FullName);
                    var valueObjectName = _proxyTriggersGenerator.CreateValueObjectTypeNameForEvent(typeDescription.FullName);
                    _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"Subscribe(proxy,\"{eventTypeName}\", ({valueObjectName} ev, BusConfig responseInfo, bool isResponse) => _bus.SendCommand(new {triggerName}(proxy.Name, ev)));");
                }
            }
        }
        private void CreateEventHandlerDeclarations()
        {
            foreach (TypeSerialization typeSerialization in _proxyGeneratorCommon.TypeSerializations.TypeSerializations)
            {
                var typeDescription = typeSerialization.GetTopLevelTypeDescription();
                if (typeDescription.IsCommand)
                {
                    var eventType = _proxyEventsGenerator.CreateEventTypeName(typeDescription.FullName);
                    _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($", IMessageHandler<{eventType}>");
                }
            }
        }
        private void CreateHandleMessageMethods()
        {
            foreach (TypeSerialization typeSerialization in _proxyGeneratorCommon.TypeSerializations.TypeSerializations)
            {
                var typeDescription = typeSerialization.GetTopLevelTypeDescription();
                if (typeDescription.IsCommand)
                {
                    var eventType = _proxyEventsGenerator.CreateEventTypeName(typeDescription.FullName);
                    _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public void HandleMessage({eventType} anEvent, BusConfig? busConfig, bool isResponse){{ Send(anEvent.ProxyName,\"{_proxyGeneratorCommon.RemoveJunk(typeDescription.FullName)}\", anEvent.Data); }}");
                }
            }
        }
    }
}
