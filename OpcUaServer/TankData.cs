using Opc.Ua;

namespace OpcUaServer;

/// <summary>A structure holding a nested structure, an array of structures and an array of built-ins.</summary>
public class TankData : IEncodeable
{
    public static readonly ExpandedNodeId TypeId =
        new ExpandedNodeId("TankDataType", DensityData.PLCNamespaceUri);
    public static readonly ExpandedNodeId BinaryEncodingId =
        new ExpandedNodeId("TankDataType_Encoding_DefaultBinary", DensityData.PLCNamespaceUri);

    public string Name { get; set; } = string.Empty;
    public DensityData Density { get; set; } = new();
    public DensityData[] History { get; set; } = [];
    public double[] Readings { get; set; } = [];

    ExpandedNodeId IEncodeable.TypeId => TypeId;
    ExpandedNodeId IEncodeable.BinaryEncodingId => BinaryEncodingId;
    ExpandedNodeId IEncodeable.XmlEncodingId => ExpandedNodeId.Null;

    public void Encode(IEncoder encoder)
    {
        encoder.WriteString("Name", Name);
        encoder.WriteEncodeable("Density", Density, typeof(DensityData));
        encoder.WriteEncodeableArray("History", History, typeof(DensityData));
        encoder.WriteDoubleArray("Readings", Readings);
    }

    public void Decode(IDecoder decoder)
    {
        Name = decoder.ReadString("Name");
        Density = (DensityData)decoder.ReadEncodeable("Density", typeof(DensityData));
        History = [.. decoder.ReadEncodeableArray("History", typeof(DensityData)).Cast<DensityData>()];
        Readings = [.. decoder.ReadDoubleArray("Readings")];
    }

    public bool IsEqual(IEncodeable encodeable) =>
        encodeable is TankData other && Name == other.Name && Density.IsEqual(other.Density)
        && History.Length == other.History.Length && History.Zip(other.History).All(pair => pair.First.IsEqual(pair.Second))
        && Readings.SequenceEqual(other.Readings);

    public object Clone() => new TankData
    {
        Name = Name,
        Density = (DensityData)Density.Clone(),
        History = [.. History.Select(d => (DensityData)d.Clone())],
        Readings = [.. Readings]
    };
}
