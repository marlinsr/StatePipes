using StatePipes.Interfaces;

namespace StatePipes.StateMachine.Internal
{
    internal class StateMachineManager
    {
        private Dictionary<Type, BaseStateMachine> _stateMachineDictionary = [];
        public void RegisterStateMachine(Type stateMachineType, BaseStateMachine stateMachine)
        {
            stateMachine.SetStateMachineManagerAndType(this, stateMachineType);
            _stateMachineDictionary.Add(stateMachineType, stateMachine);
        }
        public BaseStateMachine GetStateMachineForType(Type stateMachineType) => _stateMachineDictionary[stateMachineType];
        public BaseStateMachine GetStateMachine<StateMachineType>() where StateMachineType : IStateMachine => GetStateMachineForType(typeof(StateMachineType));
        public BaseStateMachine? GetStateMachine(string typeAssemblyQualifiedName)
        {
            var t = Type.GetType(typeAssemblyQualifiedName);
            if (t == null) return null;
            return GetStateMachineForType(t);
        }
        public List<BaseStateMachine> GetAllStateMachines() => _stateMachineDictionary.Values.ToList(); 
        public List<string> SaveAllStateMachineDotGraphToPath(string path)
        {
            List<string> ret = [];
            GetAllStateMachines().ForEach(s =>
            {
                ret.Add(s.SaveDotGraphToPath(path));
            });
            return ret;
        }
    }
}
