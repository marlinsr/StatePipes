using Autofac;
using Autofac.Util;
using StatePipes.Interfaces;
using System.Reflection;
using System.Reflection.Emit;

namespace StatePipes.StateMachine.Internal
{
    internal class BaseStateMachineAndFirstStateContainerSetup : IContainerSetup
    {
        private readonly Assembly _assembly;
        private readonly bool _sendInitAfterInitialize;
        private dynamic? _initTriggerCommand;
        private readonly Type _stateMachineType;
        private readonly Type _nextStateAfterInitType;
        private readonly Type _stateClassType;
        private readonly Type _baseTriggerCommandType;
        private readonly bool _disableAutomaticMoveToState;
        public BaseStateMachineAndFirstStateContainerSetup(Type stateMachineType, Type nextStateAfterInitType, bool sendInitAfterInitialize = true, bool disableAutomaticMoveToState = false)
        {
            _stateMachineType = stateMachineType;
            _nextStateAfterInitType = nextStateAfterInitType;
            _assembly = _stateMachineType.Assembly;
            _sendInitAfterInitialize = sendInitAfterInitialize;
            _stateClassType = GetStateClassType(_stateMachineType);
            _baseTriggerCommandType = GetTriggerClassType(_stateMachineType);
            _disableAutomaticMoveToState = disableAutomaticMoveToState;
        }
        public static Type GetStateClassType(Type stateMachineType)
        {
            Type genericBaseStateMachineStateType = typeof(BaseStateMachineState<>);
            Type[] typeArgs = [stateMachineType];
            return genericBaseStateMachineStateType.MakeGenericType(typeArgs);
        }

        private static Type GetTriggerClassType(Type stateMachineType)
        {
            Type genericBaseStateMachineStateType = typeof(BaseTriggerCommand<>);
            Type[] typeArgs = [stateMachineType];
            return genericBaseStateMachineStateType.MakeGenericType(typeArgs);
        }

        public static void RegisterOnce(ContainerBuilder containerBuilder)
        {
            _ = containerBuilder.RegisterType<StateMachineManager>().AsSelf().SingleInstance();
            _ = containerBuilder.RegisterType<BaseStateMachine>().AsSelf();
            _ = containerBuilder.RegisterType<GetAllStateMachineStatusCommandHandler>().AsSelf().AsImplementedInterfaces();
            _ = containerBuilder.RegisterType<GetAllStateMachineDiagramsCommandHandler>().AsSelf().AsImplementedInterfaces();
        }
        public void Register(ContainerBuilder containerBuilder)
        {
            RegisterStates(containerBuilder);
            RegisterCommandHandlers(containerBuilder);
            RegisterInitCommand(containerBuilder);
        }
        public void Build(IContainer container)
        {
            ConfigureStateMachine(container);
        }

        private void RegisterStates(ContainerBuilder containerBuilder)
        {
            var types = _assembly.GetLoadableTypes().Where(t => _stateClassType.IsAssignableFrom(t));
            foreach (var type in types)
            {
                containerBuilder.RegisterType(type).AsSelf().AsImplementedInterfaces().SingleInstance();
            }
        }
        private void RegisterInitCommand(ContainerBuilder containerBuilder)
        {
            var commandName = $"{_stateMachineType.Namespace}.Init{_stateMachineType.Name}Trigger";
            var aName = new AssemblyName($"{Guid.NewGuid().ToString("N")}_Init");

            AssemblyBuilder ab = AssemblyBuilder.DefineDynamicAssembly(aName, AssemblyBuilderAccess.Run);
            ModuleBuilder mb = ab.DefineDynamicModule(aName.Name!);

            TypeBuilder tb = mb.DefineType(commandName, TypeAttributes.Public);
            tb.AddInterfaceImplementation(typeof(IInitTrigger));
            tb.SetParent(_baseTriggerCommandType);

            var t = tb.CreateType();
            _ = containerBuilder.RegisterType(t).AsSelf().AsImplementedInterfaces();
            RegisterCommandHandler(containerBuilder, t);
            _initTriggerCommand = Activator.CreateInstance(t);
        }

        private void RegisterCommandHandler(ContainerBuilder containerBuilder, Type triggerCommandType)
        {
            Type genericTriggerCommandHandlerType = typeof(BaseTriggerCommandHandler<,>);
            Type[] typeArgs = [triggerCommandType, _stateMachineType];
            Type constructedTriggerCommandHandlerType = genericTriggerCommandHandlerType.MakeGenericType(typeArgs);
            _ = containerBuilder.RegisterType(constructedTriggerCommandHandlerType).AsSelf().AsImplementedInterfaces().SingleInstance();
        }

        private void RegisterCommandHandlers(ContainerBuilder containerBuilder)
        {
            var baseTriggerCommandTypeList = _assembly.GetLoadableTypes().Where(t => _baseTriggerCommandType.IsAssignableFrom(t) && !t.IsGenericType && !t.IsAbstract);
            baseTriggerCommandTypeList.ToList().ForEach(triggerCommandType => RegisterCommandHandler(containerBuilder, triggerCommandType));
        }

        private void ConfigureStateMachine(IContainer container)
        {
            var stateMachineManager = container.Resolve<StateMachineManager>();
            var stateMachine = container.Resolve<BaseStateMachine>();
            stateMachineManager.RegisterStateMachine(_stateMachineType, stateMachine);
            stateMachine.ConfigureInitialState(_nextStateAfterInitType, _initTriggerCommand);
            lock (TemporaryStateMachineHolder.StateMachineLock)
            {
                TemporaryStateMachineHolder.BaseStateMachine = stateMachine;
                var types = container.ComponentRegistry.Registrations
                  .Where(r => _stateClassType.IsAssignableFrom(r.Activator.LimitType))
                  .Select(r => r.Activator.LimitType);
                var stateList = types.Select(t => container.Resolve(t) as IStateMachineState);
                if (stateList != null) stateList.ToList().ForEach(state =>
                    {
                        if (state != null)
                        {
                            var stateWorker = new StateWorker(stateMachine, state, _disableAutomaticMoveToState);
                            stateWorker.Configure();
                        }
                    });
            }
            if (_sendInitAfterInitialize) stateMachine.SendInit();
        }
    }
}
