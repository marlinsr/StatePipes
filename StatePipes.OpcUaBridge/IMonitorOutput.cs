using Opc.Ua;
using StatePipes.Comms;
using StatePipes.OpcUaBridge.Messages;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// What a data item's MonitorStatus event says. Revised values are what the server granted, 0 unless Active;
    /// <paramref name="NodeId"/> is empty when the node is not known.
    /// </summary>
    internal sealed record MonitorStatus(string NodeId, MonitorState State, StatusCode StatusCode, double RevisedSamplingInterval, uint RevisedQueueSize, double RevisedPublishingInterval)
    {
        public static MonitorStatus Failed(string nodeId, StatusCode statusCode) => new(nodeId, MonitorState.Failed, statusCode, 0, 0, 0);
    }

    /// <summary>
    /// Where monitor responses go: the broker side of the bridge. Implementations apply the reply-to guards
    /// and never block.
    /// </summary>
    internal interface IMonitorOutput
    {
        /// <summary>Sends <paramref name="status"/> as the MonitorStatus event of the data item <paramref name="names"/> belong to.</summary>
        void SendStatus(MonitorStatus status, MessageNames names, string replyToRaw);

        /// <summary>Sends a data change as <paramref name="item"/>'s Get event.</summary>
        void SendNotification(OpcUaDataItem item, DataValue value, string replyToRaw);

        /// <summary>Whether the reply-to's response bus still exists; true when that cannot be determined.</summary>
        Task<bool> ReplyToExistsAsync(BusConfig replyTo, CancellationToken ct);
    }
}
