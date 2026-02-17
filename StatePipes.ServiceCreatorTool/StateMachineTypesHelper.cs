using System.Reflection;
using System.Runtime.Loader;

namespace StatePipes.ServiceCreatorTool
{
    internal class StateMachineTypesHelper
    {
        private readonly string _assemblyFilePath;
        private readonly string _commonFilePath;
        private readonly AssemblyLoadContext _assemblyLoadContext;
        private readonly Assembly _commonAssembly;
        private readonly IEnumerable<Type> _commonTypes;
        private readonly Assembly _assemblies;
        private readonly IEnumerable<Type> _types;
        public StateMachineTypesHelper(string projectName, PathHelper pathProvider) 
        {
            try
            {
                _assemblyFilePath = Path.Combine(pathProvider.GetPath(PathName.Bin), $"{projectName}.dll");
                _commonFilePath = Path.Combine(pathProvider.GetPath(PathName.Bin), $"StatePipes.dll");
                _assemblyLoadContext = new AssemblyLoadContext("Common");
                _commonAssembly = _assemblyLoadContext.LoadFromAssemblyPath(_commonFilePath);
                _commonTypes = _commonAssembly.GetTypesNoExceptions();
                _assemblies = _assemblyLoadContext.LoadFromAssemblyPath(_assemblyFilePath);
                _types = _assemblies.GetTypesNoExceptions();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
        }
        public string? GetStateMachineName() => GetStateMachineType()?.Name;

        public Type? GetStateMachineType()
        {
            var stateMachineOptions = GetStateMachineTypes();
            if (stateMachineOptions.Count == 1) return stateMachineOptions[0];
            if (stateMachineOptions.Count > 1)
            {
                var typeMap = stateMachineOptions.ToDictionary(t => t.Name!, t => t);
                var selected = SelectionDialog.ShowListSelection([.. typeMap.Keys], "Select State Machine:");
                if (selected != null) return typeMap[selected];
            }
            return null;
        }
        public List<Type> GetStateMachineTypes()
        {
            List<Type> stateMachineOptions = [];
            var iStateMachineType = _commonTypes.FirstOrDefault(t => t.FullName == "StatePipes.Interfaces.IStateMachine");
            var stateMachineTypes = _types.Where(t => t.IsInterface && iStateMachineType!.IsAssignableFrom(t)).ToArray();
            stateMachineOptions = [.. stateMachineTypes];
            return stateMachineOptions;
        }
        public List<Type> GetStateTypes(string stateMachineName)
        {
            List<Type> stateTypes = [];
            try
            {
                var baseCommandTriggerType = _commonTypes.FirstOrDefault(t => t.FullName == "StatePipes.Interfaces.IStateMachineState");
                stateTypes = [.. _types.Where(t =>
                {
                    if (t.IsAbstract || t.IsInterface) return false;
                    var baseType = t.BaseType;
                    if (baseType == null || !baseType.IsGenericType) return false;
                    var genArgs = baseType.GetGenericArguments();
                    return baseCommandTriggerType!.IsAssignableFrom(t) && genArgs.Length > 0 && genArgs[0].Name == stateMachineName;
                })];
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
            return stateTypes;
        }

    }
}
