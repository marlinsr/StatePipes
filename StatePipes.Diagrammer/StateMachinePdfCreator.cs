using Autofac;
using Mono.Cecil;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
namespace StatePipes.Diagrammer
{
    internal static class StateMachinePdfCreator
    {
        private static Assembly LoadDll(string dllFullPath, string outputPath)
        {
            var dllFileName = Path.GetFileName(dllFullPath);
            var dllPath = Path.GetDirectoryName(dllFullPath);
            if (dllPath == null) throw new Exception($"dllPath is null for full path {dllFullPath}!");
            var modDllFileName = $"mod_{dllFileName}";
            var dllFullPathFileName = @$"{outputPath}\{modDllFileName}";
            File.Delete(dllFullPathFileName);
            Directory.SetCurrentDirectory(dllPath);
            var parameters = new ReaderParameters();
            var assembly = ModuleDefinition.ReadModule(dllFullPath, parameters);
            var customAttribute = new CustomAttribute(assembly.ImportReference(typeof(InternalsVisibleToAttribute).GetConstructor([typeof(string)])));
            customAttribute.ConstructorArguments.Add(new CustomAttributeArgument(assembly.TypeSystem.String, AppDomain.CurrentDomain.FriendlyName));
            assembly.Assembly.CustomAttributes.Add(customAttribute);
            var customAttribute2 = new CustomAttribute(assembly.ImportReference(typeof(InternalsVisibleToAttribute).GetConstructor([typeof(string)])));
            customAttribute2.ConstructorArguments.Add(new CustomAttributeArgument(assembly.TypeSystem.String, "DynamicProxyGenAssembly2"));
            assembly.Assembly.CustomAttributes.Add(customAttribute2);
            assembly.Write(dllFullPathFileName);
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(dllFullPathFileName);
        }
        private static void BuildContainerAndDiagramsForAssembly(Assembly assembly, string outputPath, AssemblyManager assemblies)
        {
            var allStateMachineDummyContainerSetupType = assemblies.GetTypeFromAssemblies("StatePipes.StateMachine.Internal.AllStateMachineDummyContainerSetup")!;
            ContainerBuilder containerBuilder = new();
            var dummyContainerSetup = Activator.CreateInstance(allStateMachineDummyContainerSetupType, [(Assembly)assembly, DummyDependencyInjection.Create(assemblies)]);
            var allStateMachineContainerSetupType = assemblies.GetTypeFromAssemblies("StatePipes.StateMachine.Internal.AllStateMachineContainerSetup")!;
            var stateMachineContainerSetup = Activator.CreateInstance(allStateMachineContainerSetupType, [(Assembly)assembly, false]);
            var allStateMachineDummyContainerSetupRegisterMethod = allStateMachineDummyContainerSetupType.GetMethod("Register", BindingFlags.Public | BindingFlags.Instance, System.Type.DefaultBinder, [typeof(ContainerBuilder)], null);
            allStateMachineDummyContainerSetupRegisterMethod!.Invoke(dummyContainerSetup, [containerBuilder]);
            var allStateMachineContainerSetupRegisterMethod = allStateMachineContainerSetupType.GetMethod("Register", BindingFlags.Public | BindingFlags.Instance, System.Type.DefaultBinder, [typeof(ContainerBuilder)], null);
            allStateMachineContainerSetupRegisterMethod!.Invoke(stateMachineContainerSetup, [containerBuilder]);
            using var container = containerBuilder.Build();
            var allStateMachineDummyContainerSetupBuildMethod = allStateMachineDummyContainerSetupType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance, System.Type.DefaultBinder, [typeof(IContainer)], null);
            allStateMachineDummyContainerSetupBuildMethod!.Invoke(dummyContainerSetup, [container]);
            var allStateMachineContainerSetupBuildMethod = allStateMachineContainerSetupType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance, System.Type.DefaultBinder, [typeof(IContainer)], null);
            allStateMachineContainerSetupBuildMethod!.Invoke(stateMachineContainerSetup, [container]);
            var stateMachineManagerContainerSetupType = assemblies.GetTypeFromAssemblies("StatePipes.StateMachine.Internal.StateMachineManager")!;
            var stateMachineManager = container.Resolve(stateMachineManagerContainerSetupType);
            var stateMachineManagerContainerSetupSaveAllStateMachineDotGraphToPathMethod = stateMachineManagerContainerSetupType.GetMethod("SaveAllStateMachineDotGraphToPath", BindingFlags.Public | BindingFlags.Instance, System.Type.DefaultBinder, [typeof(string)], null);
            var files = (List<string>)stateMachineManagerContainerSetupSaveAllStateMachineDotGraphToPathMethod?.Invoke(stateMachineManager, [outputPath])!;
            GraphGenerator graphGenerator = new(outputPath);
            files.ForEach(file => graphGenerator.GraphStateMachine(file));
        }
        private static void CreateStateMachineDiagramsForAssembly(Assembly? assembly, string outputPath, string servicePath, AssemblyManager assemblies)
        {
            if (assembly == null) return;
            var assemblyName = assembly.GetName().Name;
            if (string.IsNullOrEmpty(assemblyName)) return;
            var dll = LoadDll(Path.Combine(servicePath, assemblyName + ".dll"), outputPath);
            if (dll == null) return;
            BuildContainerAndDiagramsForAssembly(dll, outputPath, assemblies);
        }
        public static void CreateStateMachineDiagrams(string outputPath, string servicePath)
        {
            AssemblyManager assemblies = new(servicePath);
            var iStateMachineType = assemblies.GetTypeFromAssemblies("StatePipes.Interfaces.IStateMachine")!;
            var classLibraryAssemblies = assemblies.GetAssembliesContainingType(iStateMachineType).ToList();
            classLibraryAssemblies.ForEach(classLibraryAssembly => CreateStateMachineDiagramsForAssembly(classLibraryAssembly, outputPath, servicePath, assemblies));
        }
    }
}
