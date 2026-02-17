using System.Text;

namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyGeneratorCommon
    {
        public ReferencedAssemblies Assemblies { get; private set; }
        public List<Type> Types { get; private set; }
        public TypeSerializationList TypeSerializations { get; private set; }
        public string StateMachineFullName { get; private set; }
        public string StateMachineNamespace { get; private set; }
        public PathHelper PathProvider { get; private set; }
        public string CodeGenerationBaseNamespace { get; private set; }
        public string ProxyMoniker { get; private set; }
        public StringBuilder CodeGenerationString { get;} = new StringBuilder();
        public NamespaceCollection NamespaceList { get; } = [];
        public Dictionary<string, Dictionary<string, string>> ValueObjectContructorParametersDictionary;
        public string DefaultServiceConfigurationJson { get; }
        public ProxyGeneratorCommon(string fullPathFileName, string codeGenerationBaseNamespace, string proxyMoniker, PathHelper pathProvider)
        {
            ValueObjectContructorParametersDictionary = [];
            PathProvider = pathProvider;
            CodeGenerationBaseNamespace = codeGenerationBaseNamespace;
            ProxyMoniker = proxyMoniker;
            Assemblies = new ReferencedAssemblies(fullPathFileName);
            if (Assemblies.CommandType is null || Assemblies.EventType is null) throw new Exception("Command or Event type not found in statepipes!");
            var stateMachineType = (new StateMachineTypesHelper(codeGenerationBaseNamespace, PathProvider)).GetStateMachineType() ?? throw new Exception("stateMachineType is null");
            StateMachineFullName = stateMachineType.FullName!;
            StateMachineNamespace = stateMachineType.Namespace!;
            var targetAssembly = Assemblies.GetTargetAssembly() ?? throw new Exception("Target assembly not found");
            TypeSerializations = new TypeSerializationList();
            Types = [.. targetAssembly.GetTypesNoExceptions().Where(t => t.IsPublic && !t.IsAbstract && !t.IsGenericType &&
                   (Assemblies.CommandType.IsAssignableFrom(t) || Assemblies.EventType.IsAssignableFrom(t)))];
            DefaultServiceConfigurationJson = Assemblies.GetDefaultServiceConfigurationJson();
        }
 
        public string GetTypeName(string typeFullName)
        {
            var t = Assemblies.GetTypeOf(typeFullName) ?? Assemblies.GetTypeOf(GetTypeFromFullNameString(typeFullName));
            if (t == null) return GetTypeFromFullNameString(RemoveJunk(typeFullName));
            return RemoveJunk(t.Name);
        }
        private string GetTypeFromFullNameStringJustType(string typeFullName)
        {
            NamespaceList.AddNameSpaceFromTypeFullName(typeFullName, Assemblies);
            return typeFullName.Split('.').Last();
        }
        private string GetTypeFromFullNameString(string typeFullName)
        {
            var junkRemoved = RemoveJunk(typeFullName);
            return junkRemoved.StartsWith("StatePipes.Comms") && !junkRemoved.StartsWith("StatePipes.Common.") ? junkRemoved.Replace(".", "_") : 
                GetTypeFromFullNameStringJustType(typeFullName);
        }
        private static string RemoveAferAndIncluding(string str, string delimeter)
        {
            var indexOf = str.IndexOf(delimeter);
            if (indexOf < 0) return str;
            return str[..indexOf];
        }
        public static string RemoveJunk(string typeFullName)
        {
            string ret = RemoveAferAndIncluding(typeFullName, "<");
            ret = RemoveAferAndIncluding(ret, "(");
            ret = RemoveAferAndIncluding(ret, "'");
            ret = RemoveAferAndIncluding(ret, "`");
            ret = RemoveAferAndIncluding(ret, "~");
            ret = RemoveAferAndIncluding(ret, "[");
            ret = RemoveAferAndIncluding(ret, ",");
            ret = ret.Replace("+", ".");
            return ret;
        }
        public void CreateHelperContructor(string eventName, string valueObjectName)
        {
            if (!ValueObjectContructorParametersDictionary.TryGetValue(valueObjectName, out Dictionary<string, string>? value)) return;
            string constructorString = $"public {eventName}(";
            string parameterString = string.Empty;
            foreach (var p in value)
            {
                if (!string.IsNullOrEmpty(parameterString)) parameterString += ", ";
                parameterString += $"{p.Value} {p.Key}";
            }
            constructorString += parameterString;
            if (!string.IsNullOrEmpty(parameterString)) constructorString += ", ";
            constructorString += $"string proxyName = \"\") : this(proxyName, new {valueObjectName}(";
            var thisParametersString = string.Empty;
            foreach (var p in value)
            {
                if (!string.IsNullOrEmpty(thisParametersString)) thisParametersString += ", ";
                thisParametersString += $"{p.Key}";
            }
            constructorString += $"{thisParametersString})){{}}";
            CodeGenerationString.AppendTabbedLine(constructorString);
        }
    }
}
