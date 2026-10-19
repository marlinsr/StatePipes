using Opc.Ua;

namespace StatePipes.OpcUaBridge
{
    /// <summary>Where commands are served: the OPC UA client, or a fake in tests.</summary>
    internal interface IOpcUaSession
    {
        /// <summary>Reads every node's value in one OPC UA Read, or BadNotConnected for all without a Read when there is no session.</summary>
        Task<IReadOnlyList<DataValue>> ReadValuesAsync(IReadOnlyList<NodeId> nodeIds, CancellationToken ct);

        /// <summary>
        /// Writes every value in one OPC UA Write and returns each one's result in order, or BadNotConnected for
        /// all of them without a Write when there is no session. Failures of the Write call itself are thrown.
        /// </summary>
        Task<IReadOnlyList<StatusCode>> WriteValuesAsync(IReadOnlyList<(NodeId NodeId, Variant Value)> values, CancellationToken ct);

        /// <summary>
        /// Calls one method, waiting as long as the server takes, or BadNotConnected without a call when there is
        /// no session. Failures of the Call itself are thrown.
        /// </summary>
        Task<CallMethodResult> CallAsync(NodeId objectId, NodeId methodId, IReadOnlyList<Variant> inputs, CancellationToken ct);
    }
}
