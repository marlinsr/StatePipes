using System.Runtime.Loader;

namespace StatePipes.ServiceCreatorTool
{
    internal static class StateMachineSelection
    {
        public static Type? GetStateMachineName(string projectName, PathHelper pathProvider)
        {
            List<Type> stateMachineOptions = [];
            try
            {
                var assemblyFilePath = Path.Combine(pathProvider.GetPath(PathName.Bin), $"{projectName}.dll");
                var commonFilePath = Path.Combine(pathProvider.GetPath(PathName.Bin), $"StatePipes.dll");
                var assemblyLoadContext = new AssemblyLoadContext("Common");
                var commonAssembly = assemblyLoadContext.LoadFromAssemblyPath(commonFilePath);
                var commonTypes = commonAssembly.GetTypesNoExceptions();
                var iStateMachineType = commonTypes.FirstOrDefault(t => t.FullName == "StatePipes.Interfaces.IStateMachine");
                var assemblies = assemblyLoadContext.LoadFromAssemblyPath(assemblyFilePath);
                var types = assemblies.GetTypesNoExceptions();
                var stateMachineTypes = types.Where(t => t.IsInterface && iStateMachineType!.IsAssignableFrom(t)).ToArray();
                stateMachineOptions = [.. stateMachineTypes];
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
            if (stateMachineOptions.Count == 1) return stateMachineOptions[0];
            if (stateMachineOptions.Count > 1)
            {
                var typeMap = stateMachineOptions.ToDictionary(t => t.FullName!, t => t);
                var selected = SelectionDialog.ShowListSelection([.. typeMap.Keys], "Select State Machine:");
                if (selected != null) return typeMap[selected];
                throw new OperationCanceledException();
            }
            return null;
        }
    }
}
