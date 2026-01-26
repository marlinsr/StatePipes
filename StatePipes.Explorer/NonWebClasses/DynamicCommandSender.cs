using StatePipes.Comms.Internal;
using StatePipes.SelfDescription;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Explorer.NonWebClasses
{
    internal class DynamicCommandSender
    {
        private readonly StatePipesProxyInternal _proxy;
        private readonly TypeSerializationJsonHelper _commandInstanceMgr;
        public DynamicCommandSender(StatePipesProxyInternal proxy, TypeSerializationJsonHelper commandInstanceMgr)
        {
            _proxy = proxy;
            _commandInstanceMgr = commandInstanceMgr;
        }
        public void Send(string commandJson)
        {
            try
            {
                dynamic? command = _commandInstanceMgr.GetObjectFromJson(commandJson);
                if (command == null)
                {
                    Log?.LogVerbose($"Couldn't get command for commandJson: {commandJson}");
                    return;
                }
                Log?.LogVerbose($"Sending command {command.GetType().FullName}");
                _proxy.SendCommand(command);
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
                return _commandInstanceMgr.GetObjectFromJson(commandJson);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
            return null;
        }
        public dynamic? TypeDefault(Type t)
        {
            return _commandInstanceMgr.TypeDefault(t);
        }
    }
}
