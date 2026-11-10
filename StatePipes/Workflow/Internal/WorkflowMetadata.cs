using Autofac.Util;
using StatePipes.Interfaces;
using StatePipes.StateMachine;
using StatePipes.StateMachine.Internal;
using System.Reflection;

namespace StatePipes.Workflow.Internal
{
    internal sealed class WorkflowValidationException(string message) : Exception(message);

    /// <summary>Everything the engine needs about one workflow type, computed and validated once at startup (spec §3.4, §4.2).</summary>
    internal sealed class WorkflowMetadata
    {
        private readonly Dictionary<string, HashSet<Type>> _declaredTriggers;
        private readonly Dictionary<string, string?> _parents;

        public Type WorkflowType { get; }
        public string WorkflowTypeName { get; }
        public IReadOnlyList<Type> StateTypes { get; }
        public Type FirstStateType { get; }
        public IReadOnlySet<string> FinalStateNames { get; }
        public IReadOnlyList<Type> TriggerTypes { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<FieldInfo>> PersistedFields { get; }

        private WorkflowMetadata(Type workflowType, List<Type> states, Type first, List<Type> triggers)
        {
            WorkflowType = workflowType;
            WorkflowTypeName = workflowType.FullName!;
            StateTypes = states;
            FirstStateType = first;
            FinalStateNames = states.Where(t => typeof(IFinalWorkflowState).IsAssignableFrom(t)).Select(t => t.Name).ToHashSet();
            TriggerTypes = triggers;
            PersistedFields = states.ToDictionary(t => t.Name, t => (IReadOnlyList<FieldInfo>)GetPersistedFields(t));
            _declaredTriggers = states.ToDictionary(t => t.Name, t => StateHandlerScanner.GetHandlers(t).Select(h => h.TriggerType).ToHashSet());
            _parents = states.ToDictionary(t => t.Name, t => GetParentType(t)?.Name);
        }

        public bool IsDeclared(string stateName, Type triggerType)
        {
            for (string? s = stateName; s != null; s = ParentOf(s))
                if (_declaredTriggers.TryGetValue(s, out var triggers) && triggers.Contains(triggerType)) return true;
            return false;
        }
        public string? ParentOf(string stateName) => _parents.TryGetValue(stateName, out var p) ? p : null;
        public bool IsFinal(string stateName) => FinalStateNames.Contains(stateName);

        public static WorkflowMetadata Build(Type workflowType)
        {
            var name = workflowType.Name;
            var types = workflowType.Assembly.GetLoadableTypes().Where(t => !t.IsAbstract && !t.IsInterface).ToList();
            var stateBase = typeof(BaseWorkflowState<>).MakeGenericType(workflowType);
            var states = types.Where(stateBase.IsAssignableFrom).ToList();

            var singletonState = types.FirstOrDefault(typeof(BaseStateMachineState<>).MakeGenericType(workflowType).IsAssignableFrom);
            if (singletonState != null)
                throw new WorkflowValidationException($"{singletonState.FullName} derives from BaseStateMachineState<{name}>; workflow states must derive from BaseWorkflowState<{name}>.");
            var singletonTrigger = types.FirstOrDefault(typeof(BaseTriggerCommand<>).MakeGenericType(workflowType).IsAssignableFrom);
            if (singletonTrigger != null)
                throw new WorkflowValidationException($"{singletonTrigger.FullName} derives from BaseTriggerCommand<{name}>; workflow triggers must derive from BaseWorkflowTriggerCommand<{name}>.");

            foreach (var state in states)
            {
                var ctors = state.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (ctors.Any(c => c.GetParameters().Length > 0) || !ctors.Any(c => c.GetParameters().Length == 0))
                    throw new WorkflowValidationException($"Workflow state {state.FullName} must have only a parameterless constructor (workflow states cannot use dependency injection).");
            }
            var duplicate = states.GroupBy(s => s.Name).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new WorkflowValidationException($"Workflow {workflowType.FullName} has more than one state named {duplicate.Key}: {string.Join(", ", duplicate.Select(t => t.FullName))}.");

            var firsts = states.Where(typeof(IFirstStateForStateMachine).IsAssignableFrom).ToList();
            if (firsts.Count != 1)
                throw new WorkflowValidationException($"Workflow {workflowType.FullName} must have exactly one state implementing IFirstStateForStateMachine (found {firsts.Count}).");
            if (!states.Any(typeof(IFinalWorkflowState).IsAssignableFrom))
                throw new WorkflowValidationException($"Workflow {workflowType.FullName} must have at least one state implementing IFinalWorkflowState.");

            var triggerBase = typeof(BaseWorkflowTriggerCommand<>).MakeGenericType(workflowType);
            var triggers = types.Where(t => !t.IsGenericTypeDefinition && triggerBase.IsAssignableFrom(t)).ToList();
            var settings = WorkflowJson.CreateSettings(workflowType.Assembly);
            foreach (var trigger in triggers) EnsureRoundTrips(trigger, settings);
            return new WorkflowMetadata(workflowType, states, firsts[0], triggers);
        }

        /// <summary>Workflow triggers are persisted as timer messages, so each must survive a JSON round trip.</summary>
        private static void EnsureRoundTrips(Type trigger, Newtonsoft.Json.JsonSerializerSettings settings)
        {
            try
            {
                object instance;
                try { instance = Activator.CreateInstance(trigger, nonPublic: true)!; }
                catch (MissingMethodException) { instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(trigger); }
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(instance, trigger, settings);
                Newtonsoft.Json.JsonConvert.DeserializeObject(json, trigger, settings);
            }
            catch (Exception ex)
            {
                throw new WorkflowValidationException($"Workflow trigger {trigger.FullName} does not survive JSON serialization: {ex.Message}");
            }
        }

        private static bool IsFrameworkBase(Type t) =>
            t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(BaseWorkflowState<>) || t.GetGenericTypeDefinition() == typeof(ParentedBaseWorkflowState<,>));

        private static List<FieldInfo> GetPersistedFields(Type stateType)
        {
            List<FieldInfo> fields = [];
            for (var t = stateType; t != null && !IsFrameworkBase(t); t = t.BaseType)
                fields.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            return fields;
        }

        private static Type? GetParentType(Type stateType)
        {
            for (var t = stateType.BaseType; t != null; t = t.BaseType)
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ParentedBaseWorkflowState<,>)) return t.GetGenericArguments()[1];
            return null;
        }
    }
}
