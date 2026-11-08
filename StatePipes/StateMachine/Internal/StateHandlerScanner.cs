using Mono.Cecil;
using Mono.Cecil.Cil;
using StatePipes.Comms;
using StatePipes.Interfaces;
using System.Collections.Concurrent;
using System.Reflection;

namespace StatePipes.StateMachine.Internal
{
    internal enum HandlerKind { PermitIf, IgnoreIf, PermitReentryIf }

    internal sealed record HandlerDeclaration(Type TriggerType, HandlerKind Kind, MethodInfo Method, Type? Destination, string? GuardDescription);

    /// <summary>
    /// Reflection and IL scanning shared by singleton states and workflow states: handler attributes, first substates,
    /// and the event/command types a state publishes or sends (for diagrams).
    /// </summary>
    internal static class StateHandlerScanner
    {
        private static readonly ConcurrentDictionary<string, AssemblyDefinition> _assemblyDefinitionCache = new();

        public static IReadOnlyList<HandlerDeclaration> GetHandlers(Type stateType)
        {
            List<HandlerDeclaration> result = [];
            foreach (var method in stateType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var parameters = method.GetParameters();
                if (method.GetCustomAttribute<PermitReentryIf>() is PermitReentryIf reentry)
                    result.Add(new(parameters[0].ParameterType, HandlerKind.PermitReentryIf, method, null, reentry.GuardDescription));
                if (method.GetCustomAttribute<IgnoreIf>() is IgnoreIf ignore)
                    result.Add(new(parameters[0].ParameterType, HandlerKind.IgnoreIf, method, null, ignore.GuardDescription));
                if (method.GetCustomAttribute<PermitIf>() is PermitIf permit)
                    result.Add(new(parameters[0].ParameterType, HandlerKind.PermitIf, method, permit.DestinationState, permit.GuardDescription));
            }
            return result;
        }

        /// <summary>Wires every handler declared on <paramref name="state"/> into its Stateless configuration.</summary>
        public static void Configure(object state, StateConfigurationWrapper stateConfig, Func<Type, ITrigger?> getTrigger,
            Func<BusConfig?> getResponseInfo, Type parentedOpenGeneric, Type stateBaseOpenGeneric)
        {
            var stateType = state.GetType();
            GetEventTypes(stateType, "PublishEvent").ToList().ForEach(eventType => stateConfig.RegisterEvent(eventType));
            GetEventTypes(stateType, "SendResponse").ToList().ForEach(eventType => stateConfig.RegisterEvent(eventType));
            GetCommandTypes(stateType, stateBaseOpenGeneric).ToList().ForEach(commandType => stateConfig.RegisterCommand(commandType));
            var firstSubstate = GetFirstSubstateType(stateType, parentedOpenGeneric);
            if (firstSubstate != null) stateConfig.MoveToState(firstSubstate);
            foreach (var handler in GetHandlers(stateType))
            {
                bool guard()
                {
                    var trigger = getTrigger(handler.TriggerType);
                    if (trigger == null) return false;
                    return (bool)handler.Method.Invoke(state, [trigger, getResponseInfo()])!;
                }
                stateConfig = handler.Kind switch
                {
                    HandlerKind.PermitReentryIf => stateConfig.PermitReentryIf(handler.TriggerType, guard, handler.GuardDescription),
                    HandlerKind.IgnoreIf => stateConfig.IgnoreIf(handler.TriggerType, guard, handler.GuardDescription),
                    _ => stateConfig.PermitIf(handler.TriggerType, handler.Destination!, guard, handler.GuardDescription),
                };
            }
        }

        public static Type? GetFirstSubstateType(Type stateType, Type parentedOpenGeneric)
        {
            var firstSubstateType = typeof(IFirstSubstate);
            return stateType.Assembly.GetTypes()
                .Where(t => !t.IsAbstract && firstSubstateType.IsAssignableFrom(t))
                .FirstOrDefault(t =>
                {
                    var baseType = t.BaseType;
                    while (baseType != null)
                    {
                        if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == parentedOpenGeneric)
                        {
                            var args = baseType.GetGenericArguments();
                            return args.Length >= 2 && args[1] == stateType;
                        }
                        baseType = baseType.BaseType;
                    }
                    return false;
                });
        }

        private static TypeDefinition? GetTypeDefinition(Type type)
        {
            if (type.Assembly == null) return null;
            if (!_assemblyDefinitionCache.TryGetValue(type.Assembly.Location, out var assemblyDef))
            {
                assemblyDef = AssemblyDefinition.ReadAssembly(type.Assembly.Location);
                _assemblyDefinitionCache.TryAdd(type.Assembly.Location, assemblyDef);
            }
            return assemblyDef.MainModule.Types.FirstOrDefault(t => t.FullName == type.FullName);
        }

        public static IEnumerable<Type> GetEventTypes(Type stateType, string methodName)
        {
            var typeDef = GetTypeDefinition(stateType);
            if (typeDef == null) yield break;
            foreach (var method in typeDef.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) continue;
                    if (instruction.Operand is GenericInstanceMethod genericMethod && genericMethod.Name == methodName)
                    {
                        var eventTypeRef = genericMethod.GenericArguments[0];
                        var eventType = stateType.Assembly.GetType(eventTypeRef.FullName);
                        if (eventType != null) yield return eventType;
                    }
                }
            }
        }

        public static IEnumerable<Type> GetCommandTypes(Type stateType, Type stateBaseOpenGeneric) =>
            GetEventTypes(stateType, "SendCommand").Where(t => IsCommandForAnotherStateMachine(t, stateType, stateBaseOpenGeneric));

        private static readonly Type[] _triggerOpenGenerics = [typeof(BaseTriggerCommand<>), typeof(BaseWorkflowTriggerCommand<>)];

        private static bool IsCommandForAnotherStateMachine(Type commandType, Type stateType, Type stateBaseOpenGeneric)
        {
            var currentStateMachineType = GetStateMachineTypeArgument(stateType, stateBaseOpenGeneric);
            var baseType = commandType.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType && _triggerOpenGenerics.Contains(baseType.GetGenericTypeDefinition()))
                {
                    var targetStateMachine = baseType.GetGenericArguments()[0];
                    return currentStateMachineType == null || targetStateMachine != currentStateMachineType;
                }
                baseType = baseType.BaseType;
            }
            return true; // plain ICommand (not a trigger) always goes to another entity
        }

        private static Type? GetStateMachineTypeArgument(Type stateType, Type stateBaseOpenGeneric)
        {
            var t = stateType;
            while (t != null)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == stateBaseOpenGeneric) return t.GetGenericArguments()[0];
                t = t.BaseType;
            }
            return null;
        }
    }
}
