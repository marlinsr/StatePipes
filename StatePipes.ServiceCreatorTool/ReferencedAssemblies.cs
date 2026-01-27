using System.Reflection;
using System.Runtime.Loader;

namespace StatePipes.ServiceCreatorTool
{
    internal class ReferencedAssemblies
    {
        public Type? AllStateStatusEventType { get; }
        public Type? StateStatusEventType { get; }
        public Type? GetAllStateMachineStatusCommandType { get; }
        public Type? CommandType { get; }
        public Type? EventType { get; }
        private readonly Dictionary<string, Assembly> _assemblyDictionary = [];
        private readonly Assembly? _assembly;
        private readonly TypeSerializationList _typeSerialization = new();
        private readonly Dictionary<string, Type> _typeDictionary = [];
        private readonly Assembly? _statepipesCommonAssembly;
        public ReferencedAssemblies(string dllFullFilePath)
        {
            LoadDlls(dllFullFilePath, out _statepipesCommonAssembly, out _assembly);
            TypeSerializationConverter typeSerializationConverter = new();
            if (_statepipesCommonAssembly is not null)
            {
                var types = _statepipesCommonAssembly.GetTypesNoExceptions();
                EventType = types.FirstOrDefault(t => t.FullName == "StatePipes.Interfaces.IEvent");
                CommandType = types.FirstOrDefault(t => t.FullName == "StatePipes.Interfaces.ICommand");
                AllStateStatusEventType = types.FirstOrDefault(t => t.FullName == "StatePipes.Messages.AllStateStatusEvent");
                if (AllStateStatusEventType is not null) _typeSerialization.TypeSerializations.Add(typeSerializationConverter.CreateFromType(AllStateStatusEventType, CommandType!, EventType!));
                StateStatusEventType = types.FirstOrDefault(t => t.FullName == "StatePipes.Messages.StateStatusEvent");
                if (StateStatusEventType is not null) _typeSerialization.TypeSerializations.Add(typeSerializationConverter.CreateFromType(StateStatusEventType, CommandType!, EventType!));
                GetAllStateMachineStatusCommandType = types.FirstOrDefault(t => t.FullName == "StatePipes.Messages.GetAllStateMachineStatusCommand");
                if (GetAllStateMachineStatusCommandType is not null) _typeSerialization.TypeSerializations.Add(typeSerializationConverter.CreateFromType(GetAllStateMachineStatusCommandType, CommandType!, EventType!));
            }
            LoadTypes(typeSerializationConverter);
            if (_assembly == null) throw new ArgumentException($"{dllFullFilePath} does not exist.");
        }
        private void LoadDlls(string dllFullFilePath, out Assembly? statepipesCommonAssembly, out Assembly? targetAssembly)
        {
            statepipesCommonAssembly = null;
            targetAssembly = null;
            var dllFiles = Directory.GetFiles(Path.GetDirectoryName(dllFullFilePath)!).Where(f => f.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase)).ToList();
            foreach (var dllFile in dllFiles)
            {
                try 
                {
                    var fileName = Path.GetFileName(dllFile);
                    if (fileName == "StatePipes.dll")
                    {
                        statepipesCommonAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllFile);
                        continue;
                    }
                    var assm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllFile);
                    if (assm == null) continue;
                    if (dllFile.Equals(dllFullFilePath, StringComparison.InvariantCultureIgnoreCase)) targetAssembly = assm;
                    _assemblyDictionary.Add(assm.GetName().Name!, assm);
                } catch { }
            }
        }
        private void LoadTypes(TypeSerializationConverter typeSerializationConverter)
        {
            foreach (var assm in _assemblyDictionary.Values)
            {
                var types = assm.GetTypesNoExceptions().Where(loadableType => loadableType.IsPublic);
                try
                {
                    foreach (var t in types)
                    {
                        _typeDictionary.TryAdd(t.Name, t);
                        _typeSerialization.TypeSerializations.Add(typeSerializationConverter.CreateFromType(t, CommandType!, EventType!));
                    }
                }
                catch (Exception) { }
            }
        }
        public TypeDescription? GetTypeDescription(string typeFullName)
        {
            foreach (var tsi in _typeSerialization.TypeSerializations)
            {
                try
                {
                    if (tsi.HasTypeDescription(typeFullName)) 
                        return tsi.GetTypeDescription(typeFullName);
                }
                catch { }
            }
            return null;
        }
        public Assembly? GetTargetAssembly() => _assembly;
        public Type? GetTypeOf(string name)
        {
            _typeDictionary.TryGetValue(name, out Type? ret);
            return ret;
        }
    }
}
