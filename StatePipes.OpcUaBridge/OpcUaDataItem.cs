using Opc.Ua;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// One readable OPC UA variable exposed as a StatePipes Get command / Get event pair.
    /// </summary>
    /// <param name="NodeId">The node read when the Get command arrives.</param>
    /// <param name="NodeIdText">The node in standard OPC UA notation, e.g. <c>ns=2;s=Line1.Temperature</c>.</param>
    /// <param name="MessageName">The unique name shared by the command and the event, e.g. <c>Get_ns2_s_Line1_Temperature</c>.</param>
    /// <param name="ValueType">The CLR type the event's Value is published as.</param>
    internal sealed record OpcUaDataItem(NodeId NodeId, string NodeIdText, string MessageName, Type ValueType)
    {
        public string CommandTypeFullName => $"{OpcUaNaming.CommandNamespace}.{MessageName}";
        public string EventTypeFullName => $"{OpcUaNaming.EventNamespace}.{MessageName}";
    }
}
