namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyValueObjectsGenerator
    {
        private readonly ProxyGeneratorCommon _proxyGeneratorCommon;
        private readonly ValueObjectsCreationTracker _valueObjectsCreationTracker = new();

        public ProxyValueObjectsGenerator(ProxyGeneratorCommon proxyGeneratorCommon)
        {
            _proxyGeneratorCommon = proxyGeneratorCommon;
        }
        public void CreateValueObjectsStart()
        {
            _proxyGeneratorCommon.CodeGenerationNamespace = $"{_proxyGeneratorCommon.CodeGenerationBaseNamespace}.ValueObjects.{_proxyGeneratorCommon.ProxyMoniker}";
            _proxyGeneratorCommon.NamespaceList.AddNamespace(_proxyGeneratorCommon.CodeGenerationNamespace);
            _proxyGeneratorCommon.CodeGenerationString.ResetIndention();
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"namespace {_proxyGeneratorCommon.CodeGenerationNamespace}");
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            CreateValueObjects();
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private void CreateValueObjects()
        {
            foreach (TypeSerialization typeSerialization in _proxyGeneratorCommon.TypeSerializations.TypeSerializations)
            {
                CreateValueObject(typeSerialization.GetTopLevelTypeDescription());
            }
            while (_valueObjectsCreationTracker.CountNeedsCreating > 0)
            {
                var typeQualifiedName = _valueObjectsCreationTracker.GetTopNeedsCreating()!;
                CreateSupportingValueObject(typeQualifiedName);
                _valueObjectsCreationTracker.RegisterCreatedValueObject(typeQualifiedName);
            }
        }
        private TypeDescription? GetTypeDescription(string qualifiedName) => _proxyGeneratorCommon.Assemblies.GetTypeDescription(qualifiedName);
        private void CreateSupportingValueObject(string qualifiedName)
        {
            TypeDescription? typeDescription = GetTypeDescription(qualifiedName);
            if (typeDescription == null) return;
            if (!typeDescription.Namespace.StartsWith("System") && !typeDescription.Namespace.StartsWith("StatePipes.Common"))
            {
                if (typeDescription.IsArray()) return;
                if (typeDescription.IsGeneric()) return;
                if (typeDescription.IsEnum())
                {
                    CreateValueObjectFromEnum(typeDescription);
                    return;
                }
                CreateValueObjectFromClass(typeDescription);
            }
        }
        private void CreateValueObject(TypeDescription? typeDescription)
        {
            if (typeDescription == null) return;
            if (typeDescription.IsCommand) { CreateValueObjectFromCommand(typeDescription); return; }
            if (typeDescription.IsEvent) { CreateValueObjectFromEvent(typeDescription); return; }
        }
        private void CreateValueObjectFromClass(TypeDescription? typeDescription, string postFix = "")
        {
            if (typeDescription == null) return;
            _valueObjectsCreationTracker.RegisterCreatedValueObject(typeDescription.QualifiedName);
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public class {_proxyGeneratorCommon.GetTypeName(typeDescription.QualifiedName)}{postFix}");
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            foreach (var p in typeDescription.Properties) CreateClassProperty(p);
            CreateValueObjectContructor(typeDescription, postFix);
            _proxyGeneratorCommon.CodeGenerationString.Outdent();

        }
        private string ConstructorParameterNameReplacement(string name)
        {
            if (name == "params") return "arams";
            if (name == "default") return "efault";
            return name;
        }
        private string MakeConstructorParameterName(string name)
        {
            if (!char.IsLetter(name[0])) return ConstructorParameterNameReplacement(name.Substring(1));
            if (char.IsUpper(name[0])) return ConstructorParameterNameReplacement(char.ToLower(name[0]) + name.Substring(1));
            var ret = name.ToLower();
            if (ret == name) ret = name.ToUpper();
            return ConstructorParameterNameReplacement(ret);
        }
        private void CreateValueObjectContructor(TypeDescription typeDescription, string postFix = "")
        {
            string valueObjectName = $"{_proxyGeneratorCommon.GetTypeName(typeDescription.QualifiedName)}{postFix}";
            if (typeDescription.Properties.Count <= 0)
            {
                if (!_proxyGeneratorCommon.ValueObjectContructorParametersDictionary.Keys.Contains(valueObjectName)) _proxyGeneratorCommon.ValueObjectContructorParametersDictionary.Add(valueObjectName, new());
                return;
            }
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {valueObjectName}(");
            var constructorParameterDictionary = CreateValueObjectContructorParametersDictionary(typeDescription.Properties);
            if (!_proxyGeneratorCommon.ValueObjectContructorParametersDictionary.Keys.Contains(valueObjectName)) _proxyGeneratorCommon.ValueObjectContructorParametersDictionary.Add(valueObjectName, constructorParameterDictionary);
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            typeDescription.Properties.ForEach(p => _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"{p.Name} = {MakeConstructorParameterName(p.Name)};"));
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private Dictionary<string, string> CreateValueObjectContructorParametersDictionary(List<ParameterDescription> properties)
        {
            var constructorParameterDictionary = new Dictionary<string, string>();
            for (int i = 0; i < properties.Count; i++)
            {
                string commaString = i < properties.Count - 1 ? "," : ")";
                string propertyType = GetPropertyType(properties[i].QualifiedName);
                if (properties[i].IsNullable) propertyType += "?";
                string propertyName = MakeConstructorParameterName(properties[i].Name);
                _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"   {propertyType} {propertyName}{commaString}");
                constructorParameterDictionary.Add(propertyName, propertyType);
            }
            return constructorParameterDictionary;
        }
        private void CreateClassProperty(ParameterDescription p)
        {
            string nullableString = p.IsNullable ? "?" : "";
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {GetPropertyType(p.QualifiedName)}{nullableString} {p.Name} {{ get; }}");
        }
        private string GetPropertyTypeForArray(string arrayQualifiedName, int arrayRank)
        {
            string arrayRet = GetPropertyType(arrayQualifiedName);
            string commas = string.Empty;
            for (int i = 1; i < arrayRank; i++)
            {
                commas += ",";
            }
            return $"{arrayRet}[{commas}]";
        }
        private string GetPropertyTypeForGeneric(string genericQualifiedName, TypeNames[] genericArgumentsNames)
        {
            string genericType = _proxyGeneratorCommon.GetTypeName(genericQualifiedName);
            string genericParameters = string.Empty;
            foreach (var genericArgument in genericArgumentsNames)
            {
                if (!string.IsNullOrEmpty(genericParameters)) genericParameters += ",";
                genericParameters += GetPropertyType(genericArgument.QualifiedName);
            }
            return $"{genericType}<{genericParameters}>";
        }
        private string GetPropertyType(string propertyQualifiedName)
        {
            var typeDescription = GetTypeDescription(propertyQualifiedName);
            if (typeDescription == null)
            {
                return _proxyGeneratorCommon.GetTypeName(propertyQualifiedName);
            }
            if (typeDescription.IsArray()) return GetPropertyTypeForArray(typeDescription.ArrayQualifiedName, typeDescription.ArrayRank);
            if (typeDescription.IsGeneric()) 
                return GetPropertyTypeForGeneric(typeDescription.GenericNames!.QualifiedName, typeDescription.GenericArgumentsNames);
            if (typeDescription.Namespace.StartsWith("StatePipes.Common") || typeDescription.Namespace.StartsWith("System.")) 
                return _proxyGeneratorCommon.GetTypeName(propertyQualifiedName);
            _valueObjectsCreationTracker.RegisterNeedsCreating(propertyQualifiedName);
            return _proxyGeneratorCommon.GetTypeName(propertyQualifiedName);
        }
        private void CreateValueObjectFromEnum(TypeDescription typeDescription)
        {
            _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public enum {_proxyGeneratorCommon.GetTypeName(typeDescription.QualifiedName)}");
            _proxyGeneratorCommon.CodeGenerationString.Indent();
            for (int i = 0; i < typeDescription.EnumValues.Count; i++)
            {
                string commaString = i < typeDescription.EnumValues.Count - 1 ? "," : string.Empty;
                _proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"{typeDescription.EnumValues[i].Name} = {typeDescription.EnumValues[i].Value}{commaString}");
            }
            _proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private void CreateValueObjectFromEvent(TypeDescription typeDescription) =>
            CreateValueObjectFromClass(typeDescription, $"From{_proxyGeneratorCommon.ProxyMoniker}");
        private void CreateValueObjectFromCommand(TypeDescription typeDescription) =>
            CreateValueObjectFromClass(typeDescription, $"To{_proxyGeneratorCommon.ProxyMoniker}");
    }
}
