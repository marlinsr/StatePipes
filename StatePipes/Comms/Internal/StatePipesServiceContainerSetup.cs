using Autofac;
using Autofac.Util;
using StatePipes.Interfaces;
using StatePipes.SelfDescription;
using StatePipes.StateMachine.Internal;
using System.Reflection;
namespace StatePipes.Comms.Internal
{
    internal class StatePipesServiceContainerSetup : IContainerSetup
    {
        private readonly Assembly _assembly;
        private readonly Assembly _statePipesAssebly;
        private readonly IContainerSetup _serviceContainerSetup;
        private readonly AllStateMachineContainerSetup _stateMachineContainerSetup;
        private readonly ServiceConfiguration _serviceConfiguration;
        private readonly IStatePipesProxyFactory? _parentProxyFactory;
        private readonly SelfDescriptionContainerSetup _selfDescriptionContainerSetup;
        public Assembly ClassLibraryAssembly => _assembly;
        public List<string> PublicCommandsFullName => _selfDescriptionContainerSetup.PublicCommandsFullName;
        public StatePipesServiceContainerSetup(ServiceConfiguration serviceConfiguration, IStatePipesProxyFactory? parentProxyFactory)
        {
            _serviceConfiguration = serviceConfiguration;
            _parentProxyFactory = parentProxyFactory;
            _assembly = System.Reflection.Assembly.Load(_serviceConfiguration.AssemblyName);
            var containerSetupType = _assembly.GetLoadableTypes().Single(t => t.FullName == _serviceConfiguration.ContainerSetupClassLibraryTypeFullName);
            _serviceContainerSetup = (dynamic)Activator.CreateInstance(containerSetupType)!;
            _stateMachineContainerSetup = new(_assembly, true);
            _statePipesAssebly = typeof(StatePipesService).Assembly;
            _selfDescriptionContainerSetup = new(_assembly,_statePipesAssebly);
        }
        public Dictionary<string, Type> GetPublicCommandTypeDictionary()
        {
            var types = _assembly.GetLoadableTypes().Where(t => (t.IsPublic && !t.IsAbstract && !t.IsGenericType && IsConcrete(t) &&
              typeof(ICommand).IsAssignableFrom(t) && !string.IsNullOrEmpty(t.FullName)));
            Dictionary<string, Type> commandTypeDictionary = new();
            if (types == null) return commandTypeDictionary;
            types.ToList().ForEach(t => commandTypeDictionary.Add(t.FullName!, t));
            return commandTypeDictionary;
        }
        public void Build(IContainer container)
        {
            _stateMachineContainerSetup.Build(container);
            _selfDescriptionContainerSetup.Build(container);
            _serviceContainerSetup.Build(container);
        }
        private static bool IsConcrete(Type type) => !type.IsAbstract && !type.IsInterface && !type.IsGenericTypeDefinition;
        public void Register(ContainerBuilder containerBuilder)
        {
            var handlerType = typeof(IMessageHandler<>);
            if (handlerType == null) return;
            //Register GetCurrentStatusCommand trigger
            var getCurrentStatusTriggerType = _assembly.GetLoadableTypes().FirstOrDefault(t => t.IsPublic && IsConcrete(t) &&
                    typeof(IGetCurrentStatusCommand).IsAssignableFrom(t));
            if (getCurrentStatusTriggerType != null) containerBuilder.RegisterType(getCurrentStatusTriggerType).As<IGetCurrentStatusCommand>();
            //Register all commandHandlers and eventHandlers in the app library assembly 
            var handlerList = _assembly.GetLoadableTypes().Where(t => IsConcrete(t) && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerType && i.GetGenericArguments().Any(p => p.Assembly.FullName == _assembly.FullName)));
            if (handlerList != null) handlerList.ToList().ForEach(handler => containerBuilder.RegisterType(handler).AsImplementedInterfaces().SingleInstance());
            //Register all commandHandlers and eventHandlers in the StatePipes assembly
            handlerList = _statePipesAssebly.GetLoadableTypes().Where(t => IsConcrete(t) && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerType && i.GetGenericArguments().Any(p => p.Assembly.FullName == _statePipesAssebly.FullName)));
            if (handlerList != null) handlerList.ToList().ForEach(handler => containerBuilder.RegisterType(handler).AsImplementedInterfaces().SingleInstance());
            _serviceContainerSetup.Register(containerBuilder);
            _stateMachineContainerSetup.Register(containerBuilder);
            _selfDescriptionContainerSetup.Register(containerBuilder);
            containerBuilder.RegisterInstance(new StatePipesProxyFactory(_serviceConfiguration, _parentProxyFactory)).As<IStatePipesProxyFactory>().SingleInstance();
            containerBuilder.RegisterInstance(_serviceConfiguration.Args).AsSelf().SingleInstance();
        }
    }
}
