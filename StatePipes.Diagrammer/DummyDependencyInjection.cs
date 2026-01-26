using Autofac;
using System.Reflection;
using System.Reflection.Emit;
namespace StatePipes.Diagrammer
{
    public class DummyDependencyInjection
    {
        private static Dictionary<Type, object> _dummyRepository = [];
        public static void RegisterDummyForType(ContainerBuilder builder, Type t)
        {
            if (_dummyRepository.ContainsKey(t)) return;
            var obj = FakeItEasy.Sdk.Create.Dummy(t);
            if (obj == null) return;
            builder.RegisterInstance(obj).As(t);
            _dummyRepository.Add(t, obj);
        }
        private static void BuildRegisterDummyForTypeMethod(TypeBuilder typeBuilder, Type iDummyDependencyRegistrationType)
        {
            const string registerDummyForTypeMethodName = "RegisterDummyForType";
            var myMethodBuilder = typeBuilder.DefineMethod(registerDummyForTypeMethodName,
                         MethodAttributes.Public | MethodAttributes.Virtual,
                         null,
                         [typeof(ContainerBuilder), typeof(Type)]);
            var myMethodIL = myMethodBuilder.GetILGenerator();
            myMethodIL.Emit(OpCodes.Nop);
            myMethodIL.Emit(OpCodes.Ldarg_1);
            myMethodIL.Emit(OpCodes.Ldarg_2);
            var methodToCall = typeof(DummyDependencyInjection).GetMethod(registerDummyForTypeMethodName,
                BindingFlags.Public | BindingFlags.Static, Type.DefaultBinder, [typeof(ContainerBuilder), typeof(Type)], null);
            myMethodIL.EmitCall(OpCodes.Call, methodToCall!, [typeof(ContainerBuilder), typeof(Type)]);
            myMethodIL.Emit(OpCodes.Ret);
            var registerDummyForTypeMethod = iDummyDependencyRegistrationType.GetMethod(registerDummyForTypeMethodName)!;
            typeBuilder.DefineMethodOverride(myMethodBuilder, registerDummyForTypeMethod);
        }
        internal static object Create(AssemblyManager assemblyManager)
        {
            _dummyRepository.Clear();
            var assemblyName = new AssemblyName($"{Guid.NewGuid().ToString("N")}_{typeof(DummyDependencyInjection).Name}");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
            var typeBuilder = moduleBuilder.DefineType($"{typeof(DummyDependencyInjection).FullName}2", TypeAttributes.Public);
            var iDummyDependencyRegistrationType = assemblyManager.GetTypeFromAssemblies("StatePipes.Interfaces.IDummyDependencyRegistration")!;
            typeBuilder.AddInterfaceImplementation(iDummyDependencyRegistrationType);
            BuildRegisterDummyForTypeMethod(typeBuilder, iDummyDependencyRegistrationType);
            var t = typeBuilder.CreateType();
            return Activator.CreateInstance(t)!;
        }
    }
}
