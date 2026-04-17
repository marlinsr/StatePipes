using Newtonsoft.Json;
using System.Text;

namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyGeneratorCommon
    {
        public IServiceTypeSource Source { get; private set; }
        public ReferencedAssemblies? DllAssemblies { get; private set; }
        public List<Type> Types { get; private set; }
        public TypeSerializationList TypeSerializations { get; private set; }
        public string StateMachineFullName { get; private set; }
        public string StateMachineNamespace { get; private set; }
        public PathHelper PathProvider { get; private set; }
        public string CodeGenerationBaseNamespace { get; private set; }
        public string ProxyMoniker { get; private set; }
        public StringBuilder CodeGenerationString { get; } = new StringBuilder();
        public NamespaceCollection NamespaceList { get; } = [];
        public Dictionary<string, Dictionary<string, string>> ValueObjectContructorParametersDictionary;
        public string DefaultServiceConfigurationJson { get; }
        // DLL-based constructor (existing path)
        public ProxyGeneratorCommon(string fullPathFileName, string codeGenerationBaseNamespace, string proxyMoniker, PathHelper pathProvider)
        {
            ValueObjectContructorParametersDictionary = [];
            PathProvider = pathProvider;
            CodeGenerationBaseNamespace = codeGenerationBaseNamespace;
            ProxyMoniker = proxyMoniker;
            DllAssemblies = new ReferencedAssemblies(fullPathFileName);
            Source = new DllTypeSource(DllAssemblies);
            if (DllAssemblies.CommandType is null || DllAssemblies.EventType is null) throw new Exception("Command or Event type not found in statepipes!");
            var stateMachineType = (new StateMachineTypesHelper(codeGenerationBaseNamespace, PathProvider)).GetStateMachineType() ?? throw new Exception("stateMachineType is null");
            StateMachineFullName = stateMachineType.FullName!;
            StateMachineNamespace = stateMachineType.Namespace!;
            var targetAssembly = DllAssemblies.GetTargetAssembly() ?? throw new Exception("Target assembly not found");
            TypeSerializations = new TypeSerializationList();
            Types = [.. targetAssembly.GetTypesNoExceptions().Where(t => t.IsPublic && !t.IsAbstract && !t.IsGenericType &&
                   (DllAssemblies.CommandType.IsAssignableFrom(t) || DllAssemblies.EventType.IsAssignableFrom(t)))];
            DefaultServiceConfigurationJson = DllAssemblies.GetDefaultServiceConfigurationJson();
        }
        // Live constructor — TypeSerializationList comes from a running service
        public ProxyGeneratorCommon(TypeSerializationList typeSerializationList, string codeGenerationBaseNamespace, string proxyMoniker, PathHelper pathProvider
            , string brokerUri, string exchangeName, string certPath, string certPasswordPath)
        {
            ValueObjectContructorParametersDictionary = [];
            PathProvider = pathProvider;
            CodeGenerationBaseNamespace = codeGenerationBaseNamespace;
            ProxyMoniker = proxyMoniker;
            DllAssemblies = null;
            Source = new LiveTypeSource(typeSerializationList);
            TypeSerializations = typeSerializationList;
            Types = [];
            var stateMachineType = (new StateMachineTypesHelper(codeGenerationBaseNamespace, PathProvider)).GetStateMachineType() ?? throw new Exception("stateMachineType is null");
            StateMachineFullName = stateMachineType.FullName!;
            StateMachineNamespace = stateMachineType.Namespace!;
            DefaultServiceConfigurationJson = GetDefaultServiceConfigurationJsonForLive(brokerUri, exchangeName, certPath, certPasswordPath);
        }
        private static string GetDefaultServiceConfigurationJsonForLive(string brokerUri, string exchangeName, string certPath, string certPasswordPath)
        {
            var defaultServiceConfiguration = new
            {
                BusConfig = new
                {
                    BrokerUri = brokerUri,
                    ExchangeNamePrefix = exchangeName,
                    ClientCertPath = certPath,
                    ClientCertPasswordPath = certPasswordPath,
                    ResponseExchangeGuid = Guid.NewGuid().ToString("N"),
                    ExchangeNamePostfix = "",
                    PreviousHop = (object?)null
                },
                AssemblyName = exchangeName,
                ContainerSetupClassLibraryTypeFullName = $"{exchangeName}.Builders.DefaultSetup",
                ProxyConfigurations = Array.Empty<string>(),
                ProxySubstitutions = Array.Empty<string>(),
                Args = new 
                {
                    Args = (object?)null
                }
            };
            return JsonConvert.SerializeObject(defaultServiceConfiguration).Replace("\"", "\"\"");
        }
        public string GetTypeName(string typeFullName)
        {
            var simpleName = Source.GetSimpleName(typeFullName) ?? Source.GetSimpleName(GetTypeFromFullNameString(typeFullName));
            if (simpleName == null) return GetTypeFromFullNameString(RemoveJunk(typeFullName));
            return RemoveJunk(simpleName);
        }
        private string GetTypeFromFullNameStringJustType(string typeFullName)
        {
            NamespaceList.AddNameSpaceFromTypeFullName(typeFullName, Source);
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
