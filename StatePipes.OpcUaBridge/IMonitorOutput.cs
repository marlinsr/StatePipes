using Opc.Ua;
using StatePipes.Comms;
using StatePipes.OpcUaBridge.Messages;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Where monitor responses go: the broker side of the bridge. Implementations apply the reply-to guards
    /// and never block.
    /// </summary>
    internal interface IMonitorOutput
    {
        void SendStatus(MonitorStatusEvent status, string replyToRaw);

        /// <summary>Sends a data change as <paramref name="item"/>'s Get event.</summary>
        void SendNotification(OpcUaDataItem item, DataValue value, string replyToRaw);

        /// <summary>Whether the reply-to's response bus still exists; true when that cannot be determined.</summary>
        Task<bool> ReplyToExistsAsync(BusConfig replyTo, CancellationToken ct);
    }
}
