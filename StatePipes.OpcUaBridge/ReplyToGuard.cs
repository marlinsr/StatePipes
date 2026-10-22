using StatePipes.Comms;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// The rule StatePipesService applies before answering anyone: responses only travel on this broker and
    /// under this bridge's own credentials.
    /// </summary>
    internal static class ReplyToGuard
    {
        public static bool Allows(BusConfig own, BusConfig replyTo, out string reason)
        {
            if (replyTo.BrokerUri != own.BrokerUri)
            {
                reason = $"it is on broker {replyTo.BrokerUri}";
                return false;
            }
            if (replyTo.ClientCertPath != own.ClientCertPath)
            {
                reason = $"it uses a {replyTo.ClientCertPath} certificate for authentication";
                return false;
            }
            reason = string.Empty;
            return true;
        }
    }
}
