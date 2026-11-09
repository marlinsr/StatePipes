using Newtonsoft.Json;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Internal;

namespace StatePipes.Workflow.Internal
{
    internal sealed class WorkflowMachine(BaseStateMachine machine, IReadOnlyDictionary<string, object> statesByName)
    {
        public BaseStateMachine Machine { get; } = machine;
        public IReadOnlyDictionary<string, object> StatesByName { get; } = statesByName;
        public string CurrentState => Machine.CurrentState;
    }

    /// <summary>
    /// Builds a fresh machine for one workflow command: new state objects through their parameterless constructors and a new
    /// Stateless machine, without DI (spec D14). Leaves <see cref="WorkflowContext.Current"/> set for the caller to fire in.
    /// </summary>
    internal static class WorkflowMachineFactory
    {
        public static WorkflowMachine CreateNew(WorkflowMetadata md, WorkflowContext ctxTemplate, IStatePipesService bus, InMemoryTimerService timers)
        {
            var wm = Build(md, ctxTemplate, bus, timers, typeof(InitialState).Name);
            Fire(wm, new InitTrigger());
            return wm;
        }

        public static WorkflowMachine Rehydrate(WorkflowMetadata md, WorkflowRecord record, WorkflowContext ctxTemplate, IStatePipesService bus,
            InMemoryTimerService timers, JsonSerializerSettings settings)
        {
            var wm = Build(md, ctxTemplate, bus, timers, record.CurrentState);
            WorkflowDocument.Populate(md, wm.StatesByName, record.DocumentJson, settings);
            return wm;
        }

        public static WorkflowMachine CreateForDiagram(WorkflowMetadata md, IStatePipesService bus, InMemoryTimerService timers)
        {
            try { return Build(md, new WorkflowContext(null!, string.Empty, timers), bus, timers, typeof(InitialState).Name); }
            finally { WorkflowContext.Current = null; }
        }

        /// <summary>
        /// Fires a trigger and then completes any pending MoveToState. Stateless does not run a parent's entry action when a
        /// substate re-enters its parent (the LCA is the parent), so the automatic MoveToState may not fire on its own.
        /// </summary>
        public static void Fire(WorkflowMachine wm, ITrigger trigger, StatePipes.Comms.BusConfig? responseInfo = null)
        {
            wm.Machine.Fire(trigger, responseInfo);
            while (wm.Machine.CurrentStateHasMoveToState()) wm.Machine.Fire(new MoveToState());
        }

        private static WorkflowMachine Build(WorkflowMetadata md, WorkflowContext ctxTemplate, IStatePipesService bus, InMemoryTimerService timers, string initialState)
        {
            var machine = new BaseStateMachine(bus, timers, initialState, publishStatus: false);
            machine.SetStateMachineType(md.WorkflowType);
            WorkflowContext.Current = ctxTemplate with { Machine = machine };
            var states = md.StateTypes.ToDictionary(t => t.Name, t => Activator.CreateInstance(t, nonPublic: true)!);
            foreach (var state in states.Values) new StateWorker(machine, (IStateMachineState)state, false).Configure();
            machine.ConfigureInitialState(md.FirstStateType, null);
            machine.FinalStates = md.FinalStateNames;
            return new WorkflowMachine(machine, states);
        }
    }
}
