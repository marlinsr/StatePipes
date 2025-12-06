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
            var customAttribute = new CustomAttribute(assembly.ImportReference(typeof(InternalsVisibleToAttribute).GetConstructor(new[] { typeof(string) })));
            customAttribute.ConstructorArguments.Add(new CustomAttributeArgument(assembly.TypeSystem.String, AppDomain.CurrentDomain.FriendlyName));
            assembly.Assembly.CustomAttributes.Add(customAttribute);
            var customAttribute2 = new CustomAttribute(assembly.ImportReference(typeof(InternalsVisibleToAttribute).GetConstructor(new[] { typeof(string) })));
            customAttribute2.ConstructorArguments.Add(new CustomAttributeArgument(assembly.TypeSystem.String, "DynamicProxyGenAssembly2"));
            assembly.Assembly.CustomAttributes.Add(customAttribute2);
            assembly.Write(dllFullPathFileName);
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(dllFullPathFileName);
        }
        private static void BuildContainerAndDiagramsForAssembly(Assembly assembly, string outputPath, AssemblyManager assemblies)
        {
            var allStateMachineDummyContainerSetupType = assemblies.GetTypeFromAssemblies("StatePipes.StateMachine.Internal.AllStateMachineDummyContainerSetup")!;
            ContainerBuilder containerBuilder = new();
            var dummyContainerSetup = Activator.CreateInstance(allStateMachineDummyContainerSetupType, new Object[] { (Assembly)assembly, DummyDependencyInjection.Create(assemblies) });
            var allStateMachineContainerSetupType = assemblies.GetTypeFromAssemblies("StatePipes.StateMachine.Internal.AllStateMachineContainerSetup")!;
            var stateMachineContainerSetup = Activator.CreateInstance(allStateMachineContainerSetupType, new Object[] { (Assembly)assembly, false });
            var allStateMachineDummyContainerSetupRegisterMethod = allStateMachineDummyContainerSetupType.GetMethod("Register", BindingFlags.Public | BindingFlags.Instance, System.Type.DefaultBinder, new System.Type[] { typeof(ContainerBuilder) }, null);
            allStateMachineDummyContainerSetupRegisterMethod!.Invoke(dummyContainerSetup, new object[] { containerBuilder });
            var allStateMachineContainerSetupRegisterMethod = allStateMachineContainerSetupType.GetMethod("Register", BindingFlags.Public | BindingFlags.Instance, System.Type.DefaultBinder, new System.Type[] { typeof(ContainerBuilder) }, null);
            allStateMachineContainerSetupRegisterMethod!.Invoke(stateMachineContainerSetup, new object[] { containerBuilder });
            using var container = containerBuilder.Build();
            var allStateMachineDummyContainerSetupBuildMethod = allStateMachineDummyContainerSetupType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance, System.Type.DefaultBinder, new System.Type[] { typeof(IContainer) }, null);
            allStateMachineDummyContainerSetupBuildMethod!.Invoke(dummyContainerSetup, new object[] { container });
            var allStateMachineContainerSetupBuildMethod = allStateMachineContainerSetupType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance, System.Type.DefaultBinder, new System.Type[] { typeof(IContainer) }, null);
            allStateMachineContainerSetupBuildMethod!.Invoke(stateMachineContainerSetup, new object[] { container });
            var stateMachineManagerContainerSetupType = assemblies.GetTypeFromAssemblies("StatePipes.StateMachine.Internal.StateMachineManager")!;
            var stateMachineManager = container.Resolve(stateMachineManagerContainerSetupType);
            var stateMachineManagerContainerSetupSaveAllStateMachineDotGraphToPathMethod = stateMachineManagerContainerSetupType.GetMethod("SaveAllStateMachineDotGraphToPath", BindingFlags.Public | BindingFlags.Instance, System.Type.DefaultBinder, new System.Type[] { typeof(string) }, null);
            var files = (List<string>)stateMachineManagerContainerSetupSaveAllStateMachineDotGraphToPathMethod?.Invoke(stateMachineManager, new object[] { outputPath })!;
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
