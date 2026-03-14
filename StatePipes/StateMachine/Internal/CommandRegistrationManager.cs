namespace StatePipes.StateMachine.Internal
{
    internal class CommandRegistrationManager
    {
        private readonly Dictionary<string, List<string>> _commandRegistrations = [];
        public void RegisterCommand(Type command, string state)
        {
            var baseTriggerCommandOpenType = typeof(BaseTriggerCommand<>);
            var baseType = command.BaseType;
            if (baseType == null || !baseType.IsGenericType || baseType.GetGenericTypeDefinition() != baseTriggerCommandOpenType) return;
            var targetStateMachine = baseType.GetGenericArguments()[0];
            var commandName = $"{command.Name} - {targetStateMachine.Name}";
            if (string.IsNullOrEmpty(state)) return;
            if (!_commandRegistrations.ContainsKey(state)) _commandRegistrations.Add(state, []);
            if (!_commandRegistrations[state].Contains(commandName)) _commandRegistrations[state].Add(commandName);
        }
        public IReadOnlyList<string> GetRegisteredCommands(string state)
        {
            if (!_commandRegistrations.TryGetValue(state, out List<string>? value)) return [];
            return value;
        }
    }
}
