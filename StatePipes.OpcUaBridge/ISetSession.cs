using Opc.Ua;

namespace StatePipes.OpcUaBridge
{
    /// <summary>Where Set commands are written: the OPC UA session, or a fake in tests.</summary>
    internal interface ISetSession
    {
        /// <summary>
        /// Writes every value in one OPC UA Write and returns each one's result in order, or BadNotConnected for
        /// all of them without a Write when there is no session. Failures of the Write call itself are thrown.
        /// </summary>
        Task<IReadOnlyList<StatusCode>> WriteValuesAsync(IReadOnlyList<(NodeId NodeId, Variant Value)> values, CancellationToken ct);
    }
}
