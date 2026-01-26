namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyValueObjectsGenerator(ProxyGeneratorCommon proxyGeneratorCommon)
    {
        private readonly ValueObjectsCreationTracker _valueObjectsCreationTracker = new();

        public void CreateValueObjectsStart()
        {
            proxyGeneratorCommon.CodeGenerationNamespace = $"{proxyGeneratorCommon.CodeGenerationBaseNamespace}.ValueObjects.{proxyGeneratorCommon.ProxyMoniker}";
            proxyGeneratorCommon.NamespaceList.AddNamespace(proxyGeneratorCommon.CodeGenerationNamespace);
            proxyGeneratorCommon.CodeGenerationString.ResetIndention();
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"namespace {proxyGeneratorCommon.CodeGenerationNamespace}");
            proxyGeneratorCommon.CodeGenerationString.Indent();
            CreateValueObjects();
            proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private void CreateValueObjects()
        {
            foreach (TypeSerialization typeSerialization in proxyGeneratorCommon.TypeSerializations.TypeSerializations)
            {
                CreateValueObject(typeSerialization.GetTopLevelTypeDescription());
            }
            while (_valueObjectsCreationTracker.CountNeedsCreating > 0)
            {
                var typeFullName = _valueObjectsCreationTracker.GetTopNeedsCreating()!;
                CreateSupportingValueObject(typeFullName);
                _valueObjectsCreationTracker.RegisterCreatedValueObject(typeFullName);
            }
        }
        private TypeDescription? GetTypeDescription(string typeFullName) => proxyGeneratorCommon.Assemblies.GetTypeDescription(typeFullName);
        private void CreateSupportingValueObject(string typeFullName)
        {
            TypeDescription? typeDescription = GetTypeDescription(typeFullName);
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
            _valueObjectsCreationTracker.RegisterCreatedValueObject(typeDescription.FullName);
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public class {proxyGeneratorCommon.GetTypeName(typeDescription.FullName)}{postFix}");
            proxyGeneratorCommon.CodeGenerationString.Indent();
            foreach (var p in typeDescription.Properties) CreateClassProperty(p);
            CreateValueObjectContructor(typeDescription, postFix);
            proxyGeneratorCommon.CodeGenerationString.Outdent();

        }
        private static string ConstructorParameterNameReplacement(string name)
        {
            if (name == "params") return "arams";
            if (name == "default") return "efault";
            return name;
        }
        private static string MakeConstructorParameterName(string name)
        {
            if (!char.IsLetter(name[0])) return ConstructorParameterNameReplacement(name[1..]);
            if (char.IsUpper(name[0])) return ConstructorParameterNameReplacement(char.ToLower(name[0]) + name[1..]);
            var ret = name.ToLower();
            if (ret == name) ret = name.ToUpper();
            return ConstructorParameterNameReplacement(ret);
        }
        private void CreateValueObjectContructor(TypeDescription typeDescription, string postFix = "")
        {
            string valueObjectName = $"{proxyGeneratorCommon.GetTypeName(typeDescription.FullName)}{postFix}";
            if (typeDescription.Properties.Count <= 0)
            {
                if (!proxyGeneratorCommon.ValueObjectContructorParametersDictionary.ContainsKey(valueObjectName)) proxyGeneratorCommon.ValueObjectContructorParametersDictionary.Add(valueObjectName, []);
                return;
            }
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {valueObjectName}(");
            var constructorParameterDictionary = CreateValueObjectContructorParametersDictionary(typeDescription.Properties);
            proxyGeneratorCommon.ValueObjectContructorParametersDictionary.TryAdd(valueObjectName, constructorParameterDictionary);
            proxyGeneratorCommon.CodeGenerationString.Indent();
            typeDescription.Properties.ForEach(p => proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"{p.Name} = {MakeConstructorParameterName(p.Name)};"));
            proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private Dictionary<string, string> CreateValueObjectContructorParametersDictionary(List<ParameterDescription> properties)
        {
            var constructorParameterDictionary = new Dictionary<string, string>();
            for (int i = 0; i < properties.Count; i++)
            {
                string commaString = i < properties.Count - 1 ? "," : ")";
                string propertyType = GetPropertyType(properties[i].FullName);
                if (properties[i].IsNullable) propertyType += "?";
                string propertyName = MakeConstructorParameterName(properties[i].Name);
                proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"   {propertyType} {propertyName}{commaString}");
                constructorParameterDictionary.Add(propertyName, propertyType);
            }
            return constructorParameterDictionary;
        }
        private void CreateClassProperty(ParameterDescription p)
        {
            string nullableString = p.IsNullable ? "?" : "";
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public {GetPropertyType(p.FullName)}{nullableString} {p.Name} {{ get; }}");
        }
        private string GetPropertyTypeForArray(string arrayFullName, int arrayRank)
        {
            string arrayRet = GetPropertyType(arrayFullName);
            string commas = string.Empty;
            for (int i = 1; i < arrayRank; i++)
            {
                commas += ",";
            }
            return $"{arrayRet}[{commas}]";
        }
        private string GetPropertyTypeForGeneric(string genericFullName, TypeNames[] genericArgumentsNames)
        {
            string genericType = proxyGeneratorCommon.GetTypeName(genericFullName);
            string genericParameters = string.Empty;
            foreach (var genericArgument in genericArgumentsNames)
            {
                if (!string.IsNullOrEmpty(genericParameters)) genericParameters += ",";
                genericParameters += GetPropertyType(genericArgument.FullName);
            }
            return $"{genericType}<{genericParameters}>";
        }
        private string GetPropertyType(string propertyFullName)
        {
            var typeDescription = GetTypeDescription(propertyFullName);
            if (typeDescription == null)
            {
                return proxyGeneratorCommon.GetTypeName(propertyFullName);
            }
            if (typeDescription.IsArray()) return GetPropertyTypeForArray(typeDescription.ArrayFullName, typeDescription.ArrayRank);
            if (typeDescription.IsGeneric()) 
                return GetPropertyTypeForGeneric(typeDescription.GenericNames!.FullName, typeDescription.GenericArgumentsNames);
            if (typeDescription.Namespace.StartsWith("StatePipes.Common") || typeDescription.Namespace.StartsWith("System.")) 
                return proxyGeneratorCommon.GetTypeName(propertyFullName);
            _valueObjectsCreationTracker.RegisterNeedsCreating(propertyFullName);
            return proxyGeneratorCommon.GetTypeName(propertyFullName);
        }
        private void CreateValueObjectFromEnum(TypeDescription typeDescription)
        {
            proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"public enum {proxyGeneratorCommon.GetTypeName(typeDescription.FullName)}");
            proxyGeneratorCommon.CodeGenerationString.Indent();
            for (int i = 0; i < typeDescription.EnumValues.Count; i++)
            {
                string commaString = i < typeDescription.EnumValues.Count - 1 ? "," : string.Empty;
                proxyGeneratorCommon.CodeGenerationString.AppendTabbedLine($"{typeDescription.EnumValues[i].Name} = {typeDescription.EnumValues[i].Value}{commaString}");
            }
            proxyGeneratorCommon.CodeGenerationString.Outdent();
        }
        private void CreateValueObjectFromEvent(TypeDescription typeDescription) =>
            CreateValueObjectFromClass(typeDescription, $"From{proxyGeneratorCommon.ProxyMoniker}");
        private void CreateValueObjectFromCommand(TypeDescription typeDescription) =>
            CreateValueObjectFromClass(typeDescription, $"To{proxyGeneratorCommon.ProxyMoniker}");
    }
}
