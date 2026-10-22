using Opc.Ua;
using StatePipes.Comms;

namespace StatePipes.OpcUaBridge
{
    /// <summary>Where command answers go: the broker side of the bridge, which never blocks.</summary>
    internal interface ICommandOutput
    {
        /// <summary>Sends <paramref name="value"/> as <paramref name="item"/>'s Get event to the Get's sender.</summary>
        void SendGetEvent(OpcUaDataItem item, DataValue value, BusConfig replyTo, byte[] replyToRaw);

        /// <summary>Sends <paramref name="statusCode"/> as <paramref name="item"/>'s SetStatus event to the Set's sender.</summary>
        void SendSetStatus(OpcUaDataItem item, StatusCode statusCode, BusConfig replyTo, byte[] replyToRaw);

        /// <summary>Sends <paramref name="body"/> as the ExecResult event of the method named by <paramref name="names"/> to the Exec's sender.</summary>
        void SendExecResult(MessageNames names, byte[] body, BusConfig replyTo, byte[] replyToRaw);
    }
}
