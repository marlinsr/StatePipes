using Opc.Ua;

namespace OpcUaServer;

public class DensityData : IEncodeable
{
    public const string PLCNamespaceUri = "http://plcsimulator.org/";

    public static readonly ExpandedNodeId TypeId =
        new ExpandedNodeId("DensityDataType", PLCNamespaceUri);
    public static readonly ExpandedNodeId BinaryEncodingId =
        new ExpandedNodeId("DensityDataType_Encoding_DefaultBinary", PLCNamespaceUri);

    public double Volume { get; set; }
    public double Mass { get; set; }

    ExpandedNodeId IEncodeable.TypeId => TypeId;
    ExpandedNodeId IEncodeable.BinaryEncodingId => BinaryEncodingId;
    ExpandedNodeId IEncodeable.XmlEncodingId => ExpandedNodeId.Null;

    public void Encode(IEncoder encoder)
    {
        encoder.WriteDouble("Volume", Volume);
        encoder.WriteDouble("Mass", Mass);
    }

    public void Decode(IDecoder decoder)
    {
        Volume = decoder.ReadDouble("Volume");
        Mass = decoder.ReadDouble("Mass");
    }

    public bool IsEqual(IEncodeable encodeable) =>
        encodeable is DensityData other && Volume == other.Volume && Mass == other.Mass;

    public object Clone() => new DensityData { Volume = Volume, Mass = Mass };
}
