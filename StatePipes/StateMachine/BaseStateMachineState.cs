using Mono.Cecil;
using Mono.Cecil.Cil;
using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Internal;
using System.Collections.Concurrent;
using System.Reflection;

namespace StatePipes.StateMachine
{
    public class BaseStateMachineState<StateMachineType> : IStateMachineState where StateMachineType : IStateMachine
    {
        private static readonly ConcurrentDictionary<string, AssemblyDefinition> _assemblyDefinitionCache = new();
#pragma warning disable IDE1006 // Naming Styles
        private BaseStateMachine? _stateMachine { get; set; }
#pragma warning restore IDE1006 // Naming Styles
        public BaseStateMachineState()
        {
            if (TemporaryStateMachineHolder.BaseStateMachine == null) throw new Exception("TemporaryStateMachineHolder.BaseStateMachine == null");
            _stateMachine = TemporaryStateMachineHolder.BaseStateMachine;
        }
        protected bool Fire<TTrigger>(TTrigger trigger, BusConfig? responseInfo = null) where TTrigger : ITrigger => _stateMachine?.Fire(trigger, responseInfo) ?? false;
        protected bool FireExternal<TStateMachine, BaseTriggerCommandType>(BaseTriggerCommandType trigger, BusConfig? responseInfo = null) 
            where TStateMachine : IStateMachine where BaseTriggerCommandType : BaseTriggerCommand<TStateMachine> =>
            _stateMachine?.FireExternal<TStateMachine, BaseTriggerCommandType>(trigger, responseInfo) ?? false;
        protected void SendCurrentStatusAllStateMachines() => _stateMachine?.SendCurrentStatusAllStateMachines();
        protected string GetCurrentStateForExternal<TStateMachine>() where TStateMachine : IStateMachine => _stateMachine?.CurrentState ?? string.Empty;
        protected void SendCommand<TCommand>(TCommand trigger, BusConfig? responseInfo = null) where TCommand : class, ICommand => _stateMachine?.SendCommand(trigger, responseInfo);
        protected void PublishEvent<TEvent>(TEvent ev) where TEvent : class, IEvent => _stateMachine?.PublishEvent(ev);
        protected void SendResponse<TEvent>(TEvent ev, BusConfig responseInfo) where TEvent : class, IEvent => _stateMachine?.SendResponse(ev, responseInfo);
        protected void SendCurrentStatus() => _stateMachine?.SendCurrentStatus();
        protected IDelayedMessageSender<TMessage>? CreateDelayedMessageSender<TMessage>() where TMessage : class, IMessage => _stateMachine?.CreateDelayedMessageSender<TMessage>();
        protected TTrigger? GetCurrentTrigger<TTrigger>() where TTrigger : ITrigger => (TTrigger?)GetCurrentTrigger(typeof(TTrigger));
        internal ITrigger? GetCurrentTrigger(Type triggerType)
        { 
            if (_stateMachine == null) return default;
            return _stateMachine.GetCurrentTrigger(triggerType);
        }
        protected BusConfig? GetCurrentResponseInfo()
        {
            if (_stateMachine == null) return null;
            return _stateMachine.GetCurrentResponseInfo();
        }
        public virtual void Configure(StateConfigurationWrapper stateConfig)
        {
            var stateType = this.GetType();
            if (stateType == null) return;
            GetEventTypesForState(stateType, "PublishEvent").ToList().ForEach(eventType => stateConfig.RegisterEvent(eventType));
            GetEventTypesForState(stateType, "SendResponse").ToList().ForEach(eventType => stateConfig.RegisterEvent(eventType));
            stateConfig.OnActivate(OnActivate).OnDeactivate(OnDeActivate).OnEntry(OnEntry).OnExit(OnExit);
            var firstSubstate = GetFirstSubstateType(stateType);
            if (firstSubstate != null) stateConfig.MoveToState(firstSubstate);
            var methods = stateType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if(methods == null) return;
            foreach (var method in methods)
            {
                HandlePermitReentryIfMethod(method, stateConfig);
                HandleIgnoreIfMethod(method, stateConfig);
                HandlePermitIfMethod( method, stateConfig);
            }
        }
        private void HandlePermitIfMethod(MethodInfo method, StateConfigurationWrapper stateConfig)
        {
            var permitIfAttr = method.GetCustomAttribute<PermitIf>();
            if (permitIfAttr == null) return;
            var triggerType = method.GetParameters()[0].ParameterType;
            var guardMethod = method; // capture for closure
            bool guard()
            {
                var trigger = GetCurrentTrigger(triggerType);
                if (trigger == null) return false;
                var busConfig = GetCurrentResponseInfo();
                return (bool)guardMethod.Invoke(this, [trigger, busConfig])!;
            }
            stateConfig = stateConfig.PermitIf(triggerType, permitIfAttr.DestinationState, guard, permitIfAttr.GuardDescription);
        }
        private void HandleIgnoreIfMethod(MethodInfo method, StateConfigurationWrapper stateConfig)
        {
            var ignoreIfAttr = method.GetCustomAttribute<IgnoreIf>();
            if (ignoreIfAttr == null) return;
            var triggerType = method.GetParameters()[0].ParameterType;
            var guardMethod = method; // capture for closure
            bool guard()
            {
                var trigger = GetCurrentTrigger(triggerType);
                if (trigger == null) return false;
                var busConfig = GetCurrentResponseInfo();
                return (bool)guardMethod.Invoke(this, [trigger, busConfig])!;
            }
            stateConfig = stateConfig.IgnoreIf(triggerType, guard, ignoreIfAttr.GuardDescription);
        }
        private void HandlePermitReentryIfMethod(MethodInfo method, StateConfigurationWrapper stateConfig)
        {
            var permitReentryIfAttr = method.GetCustomAttribute<PermitReentryIf>();
            if (permitReentryIfAttr == null) return;
            var triggerType = method.GetParameters()[0].ParameterType;
            var guardMethod = method; // capture for closure
            bool guard()
            {
                var trigger = GetCurrentTrigger(triggerType);
                if (trigger == null) return false;
                var busConfig = GetCurrentResponseInfo();
                return (bool)guardMethod.Invoke(this, [trigger, busConfig])!;
            }
            stateConfig = stateConfig.PermitReentryIf(triggerType, guard, permitReentryIfAttr.GuardDescription);
        }
        private static Type? GetFirstSubstateType(Type thisType)
        {
            var parentedBaseType = typeof(ParentedBaseStateMachineState<,>);
            var firstSubstateType = typeof(IFirstSubstate);
            return thisType.Assembly.GetTypes()
                .Where(t => !t.IsAbstract && firstSubstateType.IsAssignableFrom(t))
                .FirstOrDefault(t =>
                {
                    var baseType = t.BaseType;
                    while (baseType != null)
                    {
                        if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == parentedBaseType)
                        {
                            var args = baseType.GetGenericArguments();
                            return args.Length >= 2 && args[1] == thisType;
                        }
                        baseType = baseType.BaseType;
                    }
                    return false;
                });
        }
        private static TypeDefinition? GetTypeDefinition(Type thisType)
        {
            if (thisType.Assembly == null) return null;
            if (!_assemblyDefinitionCache.TryGetValue(thisType.Assembly.Location, out var assemblyDef))
            {
                assemblyDef = AssemblyDefinition.ReadAssembly(thisType.Assembly.Location);
                _assemblyDefinitionCache.TryAdd(thisType.Assembly.Location, assemblyDef);
            }
            return assemblyDef.MainModule.Types.FirstOrDefault(t => t.FullName == thisType.FullName);
        }
        protected static IEnumerable<Type> GetEventTypesForState(Type thisType, string methodName)
        {
            var typeDef = GetTypeDefinition(thisType);
            if (typeDef == null) yield break;
            foreach (var method in typeDef.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) continue;
                    if (instruction.Operand is GenericInstanceMethod genericMethod && genericMethod.Name == methodName)
                    {
                        var eventTypeRef = genericMethod.GenericArguments[0];
                        var eventType = thisType.Assembly.GetType(eventTypeRef.FullName);
                        if (eventType != null) yield return eventType;
                    }
                }
            }
        }
        public virtual void OnEntry() { }
        public virtual void OnExit() { }
        public virtual void OnActivate() { }
        public virtual void OnDeActivate() { }
    }
}

