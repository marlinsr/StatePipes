using StatePipes.Comms.Internal;
using StatePipes.SelfDescription;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Explorer.NonWebClasses
{
    internal class DynamicCommandSender(StatePipesProxyInternal proxy, TypeSerializationJsonHelper commandInstanceMgr)
    {
        public void Send(string commandJson)
        {
            try
            {
                dynamic? command = commandInstanceMgr.GetObjectFromJson(commandJson);
                if (command == null)
                {
                    Log?.LogVerbose($"Couldn't get command for commandJson: {commandJson}");
                    return;
                }
                Log?.LogVerbose($"Sending command {command.GetType().FullName}");
                proxy.SendCommand(command);
            }
            catch (Exception ex) 
            { 
                Log?.LogException(ex);
            }
        }
        public object? GetCommandObject(string commandJson)
        {
            try
            {
                return commandInstanceMgr.GetObjectFromJson(commandJson);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
            return null;
        }
        public dynamic? TypeDefault(Type t)
        {
            return commandInstanceMgr.TypeDefault(t);
        }
    }
}
