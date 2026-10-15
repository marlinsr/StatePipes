using StatePipes.Interfaces;

namespace StatePipes.OpcUaBridge.Messages
{
    /// <summary>Whether an OPC UA monitored item samples and reports, or is kept on the server but silent.</summary>
    public enum MonitoringModeKind { Disabled, Reporting }

    /// <summary>Which parts of a value make the server report a data change (OPC UA DataChangeTrigger).</summary>
    public enum DataChangeTriggerKind { Status, StatusValue, StatusValueTimestamp }

    /// <summary>How small a value change the server suppresses (OPC UA DeadbandType).</summary>
    public enum DeadbandTypeKind { None, Absolute, Percent }

    /// <summary>
    /// Where a monitor stands. Pending: stored, waiting for an OPC UA session. Suspended: its reply-to no
    /// longer exists, so it is kept but silent until the sender sends its Monitor command again.
    /// </summary>
    public enum MonitorState { Active, Pending, Failed, Suspended, Deleted }

    /// <summary>Deletes the sender's monitor with this name.</summary>
    public class DeleteMonitorCommand(string monitorName) : ICommand
    {
        public string MonitorName { get; } = monitorName;
    }

    /// <summary>
    /// The response to a Monitor or Delete command, and what each sender is told about its monitors after they
    /// are rebuilt on a new OPC UA session. Revised values are what the server granted, 0 unless Active.
    /// </summary>
    public class MonitorStatusEvent(string monitorName, string nodeId, MonitorState state, uint statusCode, string status,
        double revisedSamplingInterval, uint revisedQueueSize, double revisedPublishingInterval) : IEvent
    {
        public string MonitorName { get; } = monitorName;
        public string NodeId { get; } = nodeId;
        public MonitorState State { get; } = state;
        public uint StatusCode { get; } = statusCode;
        public string Status { get; } = status;
        public double RevisedSamplingInterval { get; } = revisedSamplingInterval;
        public uint RevisedQueueSize { get; } = revisedQueueSize;
        public double RevisedPublishingInterval { get; } = revisedPublishingInterval;
    }
}
