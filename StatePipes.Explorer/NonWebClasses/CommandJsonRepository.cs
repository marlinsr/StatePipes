using StatePipes.SelfDescription;

namespace StatePipes.Explorer.NonWebClasses
{
    internal class CommandJsonRepository
    {
        private readonly List<CommandEntry> _commandList = [];
        public CommandJsonRepository() { }
        private void AddCommandEntry(TypeSerializationJsonHelper t)
        {
            string name = t.ThisType?.FullName ?? string.Empty;
            try
            {
                string json = t.GenerateExampleJson();
                var commandListCmd = _commandList.FirstOrDefault(t => t.FullName == name);
                if (commandListCmd != null)
                {
                    if (commandListCmd.OriginalJson != json)
                    {
                        _commandList.Remove(commandListCmd);
                        _commandList.Add(new CommandEntry(name, json));
                    }
                }
                else _commandList.Add(new CommandEntry(name, json));
            }
            catch (Exception ex) { _commandList.Add(new CommandEntry(name, ex.Message));}
        }
        public void SetJsonStrings(List<TypeSerializationJsonHelper> tList)
        {
            lock (_commandList)
            {
                tList.ForEach(t => AddCommandEntry(t));
                List<CommandEntry> commandListToRemove = [];
                _commandList.ForEach(cmd =>
                {
                    if (tList.FirstOrDefault(tCmd => tCmd.ThisType?.FullName == cmd.FullName) == null) commandListToRemove.Add(cmd);
                });
                commandListToRemove.ForEach(cmd =>_commandList.Remove(cmd));
            }
        }

        public void ResetJson(string commandTypeFullName)
        {
            lock (_commandList)
            {
                var commandListCmd = _commandList.FirstOrDefault(t => t.FullName == commandTypeFullName);
                if (commandListCmd != null)
                {
                    commandListCmd.ResetJson();
                }
            }
        }

        public List<CommandEntry> GetCommandList()
        {
            lock (_commandList)
            {
                List<CommandEntry> commandListClone = [.. _commandList];
                return commandListClone;
            }
        }
    }
}
