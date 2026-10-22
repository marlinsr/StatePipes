using Opc.Ua;

namespace StatePipes.OpcUaBridge
{
    /// <summary>One input or output argument of an exposed method, as a property of its Exec command or ExecResult event.</summary>
    /// <param name="PropertyName">The property the argument travels as (see <see cref="MethodArgumentNaming"/>).</param>
    /// <param name="BuiltInType">The argument's built-in type, which an input is encoded as exactly.</param>
    /// <param name="ValueType">The CLR type the argument is published as, unless it is a structure.</param>
    /// <param name="Structure">For a structured argument, the layout it is encoded and decoded as.</param>
    /// <param name="IsArray">Whether the argument is a one-dimensional array (of <paramref name="Structure"/> for a structured one).</param>
    internal sealed record MethodArgument(string PropertyName, BuiltInType BuiltInType, Type ValueType, StructureLayout? Structure, bool IsArray);

    /// <summary>
    /// One OPC UA method, called on one object, exposed as a StatePipes Exec command / ExecResult event pair.
    /// </summary>
    /// <param name="ObjectId">The object the method is called on.</param>
    /// <param name="MethodId">The method called.</param>
    /// <param name="Names">The unique namespace and name of the command and event (see <see cref="OpcUaNaming.AssignUniqueMethodNames"/>).</param>
    internal sealed record OpcUaMethodItem(NodeId ObjectId, NodeId MethodId, string ObjectIdText, string MethodIdText, MessageNames Names,
        IReadOnlyList<MethodArgument> Inputs, IReadOnlyList<MethodArgument> Outputs)
    {
        public string ExecCommandTypeFullName => Names.ExecCommandTypeFullName;
        public string ExecResultEventTypeFullName => Names.ExecResultEventTypeFullName;
    }

    /// <summary>What one discovery of the server found: the readable variables and the executable methods.</summary>
    internal sealed record DiscoveredNodes(IReadOnlyList<OpcUaDataItem> Items, IReadOnlyList<OpcUaMethodItem> Methods);
}
