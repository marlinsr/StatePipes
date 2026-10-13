using Opc.Ua;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// One readable OPC UA variable exposed as a StatePipes Get command / Get event pair.
    /// </summary>
    /// <param name="NodeId">The node read when the Get command arrives.</param>
    /// <param name="NodeIdText">The node in standard OPC UA notation, e.g. <c>ns=2;s=Line1.Temperature</c>.</param>
    /// <param name="Names">The unique namespace and name of the command and event (see <see cref="OpcUaNaming"/>).</param>
    /// <param name="ValueType">The CLR type the event's Value is published as, unless it is a structure.</param>
    /// <param name="Structure">For a structured variable, the layout its Value is published and decoded as.</param>
    /// <param name="IsArray">Whether a structured variable holds a one-dimensional array of <paramref name="Structure"/>.</param>
    internal sealed record OpcUaDataItem(NodeId NodeId, string NodeIdText, MessageNames Names, Type ValueType, StructureLayout? Structure = null, bool IsArray = false)
    {
        public string CommandTypeFullName => Names.CommandTypeFullName;
        public string EventTypeFullName => Names.EventTypeFullName;
    }
}
