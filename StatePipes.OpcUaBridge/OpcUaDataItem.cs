using Opc.Ua;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// One readable OPC UA variable exposed as a StatePipes Get command / Get event pair.
    /// </summary>
    /// <param name="NodeId">The node read when the Get command arrives.</param>
    /// <param name="NodeIdText">The node in standard OPC UA notation, e.g. <c>ns=2;s=Line1.Temperature</c>.</param>
    /// <param name="Names">The unique namespace and name of the command and event (see <see cref="OpcUaNaming"/>).</param>
    /// <param name="ValueType">The CLR type the event's Value is published as.</param>
    internal sealed record OpcUaDataItem(NodeId NodeId, string NodeIdText, MessageNames Names, Type ValueType)
    {
        public string CommandTypeFullName => Names.CommandTypeFullName;
        public string EventTypeFullName => Names.EventTypeFullName;
    }
}
