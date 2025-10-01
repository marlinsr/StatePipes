using Autofac;
using Autofac.Util;
using StatePipes.Interfaces;
using StatePipes.Messages;
using System.Reflection;

namespace StatePipes.SelfDescription
{
    internal class SelfDescriptionContainerSetup(Assembly assembly, Assembly statePipesAssebly) : IContainerSetup
    {
        public List<string> PublicCommandsFullName { get; private set; } = new List<string>();
        public void Build(IContainer container)
        {
        }
        private static bool IsConcrete(Type type) => !type.IsAbstract && !type.IsInterface && !type.IsGenericTypeDefinition;
        private void TypeRegistrationWorker(Assembly asm, TypeSerializationList typeSerializations, TypeSerializationConverter typeSerializationConverter)
        {
            var types = asm.GetLoadableTypes().Where(t => t.IsPublic && !t.IsAbstract && !t.IsGenericType && IsConcrete(t) &&
              (typeof(ICommand).IsAssignableFrom(t) || typeof(IEvent).IsAssignableFrom(t)));
            if (types == null) return; 
            types.ToList().ForEach(t => {
                typeSerializations.TypeSerializations.Add(typeSerializationConverter.CreateFromType(t));
                if (typeof(ICommand).IsAssignableFrom(t) && !string.IsNullOrEmpty(t.FullName)) PublicCommandsFullName.Add(t.FullName);
            });
        }
        public void Register(ContainerBuilder containerBuilder)
        {
            TypeSerializationList typeSerializations = new();
            TypeSerializationConverter typeSerializationConverter = new();
            TypeRegistrationWorker(assembly, typeSerializations, typeSerializationConverter);
            //Self describe public events and commands in statepipes
            TypeRegistrationWorker(statePipesAssebly, typeSerializations, typeSerializationConverter);
            containerBuilder.RegisterInstance(typeSerializations).SingleInstance().AsSelf();
        }
    }
}
