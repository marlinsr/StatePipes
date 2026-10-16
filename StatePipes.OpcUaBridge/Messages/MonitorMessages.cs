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
}
