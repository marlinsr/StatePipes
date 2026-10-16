using StatePipes.Common;
using StatePipes.Comms;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Identifies a monitor: each sender, known by the command exchange its reply-to names, has at most one monitor
    /// per data item, known by its NodeId in standard notation (stable across rediscovery, unlike message names).
    /// </summary>
    internal readonly record struct MonitorKey(string SenderCommandExchange, string NodeIdText);

    /// <summary>
    /// A monitor as a client asked for it, which is what the bridge persists and rebuilds on every OPC UA session.
    /// </summary>
    /// <param name="ReplyToRaw">The sender's reply-to header as received, sent back unchanged with every response.</param>
    /// <param name="SuspendedSince">When the reply-to was found missing, or null while the monitor is live.</param>
    internal sealed record MonitorDefinition(MonitorKey Key, MonitorParameters Parameters, string ReplyToRaw, DateTime? SuspendedSince)
    {
        public string NodeIdText => Key.NodeIdText;

        /// <summary>The reply-to parsed from <see cref="ReplyToRaw"/>, or null if it is not a BusConfig.</summary>
        public BusConfig? ReplyTo
        {
            get
            {
                try { return JsonUtility.GetObjectForJsonString<BusConfig>(ReplyToRaw); }
                catch (Exception) { return null; }
            }
        }

        public bool IsSuspended => SuspendedSince.HasValue;
    }
}
