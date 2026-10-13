using Opc.Ua;
using Opc.Ua.Server;

namespace OpcUaServer;

public class PlcNodeManager : CustomNodeManager2
{
    private BaseDataVariableState<double> _pressure = null!;
    private BaseDataVariableState<string> _msg = null!;
    private BaseDataVariableState<ExtensionObject> _density = null!;

    public PlcNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, "http://plcsimulator.org/")
    {
        EncodeableFactory.GlobalFactory.AddEncodeableType(DensityData.BinaryEncodingId, typeof(DensityData));
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();

            var root = new FolderState(null);
            root.Create(
                SystemContext,
                new NodeId("PLC_Data", NamespaceIndex),
                new QualifiedName("PLC_Data", NamespaceIndex),
                new LocalizedText("PLC_Data"),
                false);
            root.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, root.NodeId));
            AddPredefinedNode(SystemContext, root);

            _pressure = new BaseDataVariableState<double>(root);
            _pressure.Create(
                SystemContext,
                new NodeId("pressure", NamespaceIndex),
                new QualifiedName("pressure", NamespaceIndex),
                new LocalizedText("pressure"),
                false);
            _pressure.DataType = DataTypeIds.Double;
            _pressure.ValueRank = ValueRanks.Scalar;
            _pressure.AccessLevel = AccessLevels.CurrentReadOrWrite;
            _pressure.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            _pressure.Value = 0.0;
            _pressure.StatusCode = StatusCodes.Good;
            _pressure.Timestamp = DateTime.UtcNow;
            root.AddChild(_pressure);
            AddPredefinedNode(SystemContext, _pressure);

            _msg = new BaseDataVariableState<string>(root);
            _msg.Create(
                SystemContext,
                new NodeId("msg", NamespaceIndex),
                new QualifiedName("msg", NamespaceIndex),
                new LocalizedText("msg"),
                false);
            _msg.DataType = DataTypeIds.String;
            _msg.ValueRank = ValueRanks.Scalar;
            _msg.AccessLevel = AccessLevels.CurrentReadOrWrite;
            _msg.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            _msg.Value = "Ready";
            _msg.StatusCode = StatusCodes.Good;
            _msg.Timestamp = DateTime.UtcNow;
            root.AddChild(_msg);
            AddPredefinedNode(SystemContext, _msg);

            var densityTypeId = CreateDensityDataType(externalReferences);

            // Density variable — single node whose value is a DensityData structure
            _density = new BaseDataVariableState<ExtensionObject>(root);
            _density.Create(
                SystemContext,
                new NodeId("Density", NamespaceIndex),
                new QualifiedName("Density", NamespaceIndex),
                new LocalizedText("Density"),
                false);
            _density.DataType = densityTypeId;
            _density.ValueRank = ValueRanks.Scalar;
            _density.AccessLevel = AccessLevels.CurrentReadOrWrite;
            _density.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            _density.Value = new ExtensionObject(DensityData.BinaryEncodingId, new DensityData { Volume = 1.0, Mass = 2.0 });
            _density.StatusCode = StatusCodes.Good;
            _density.Timestamp = DateTime.UtcNow;
            root.AddChild(_density);
            AddPredefinedNode(SystemContext, _density);

            var calcMethod = new MethodState(root);
            calcMethod.Create(
                SystemContext,
                new NodeId("CalculatePressureVolumeMass", NamespaceIndex),
                new QualifiedName("CalculatePressureVolumeMass", NamespaceIndex),
                new LocalizedText("CalculatePressureVolumeMass"),
                false);
            calcMethod.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            calcMethod.InputArguments = new PropertyState<Argument[]>(calcMethod);
            calcMethod.InputArguments.Create(
                SystemContext,
                new NodeId("CalculatePressureVolumeMass_InputArguments", NamespaceIndex),
                new QualifiedName(BrowseNames.InputArguments),
                null,
                false);
            calcMethod.InputArguments.Value =
            [
                new Argument { Name = "temperature", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar }
            ];
            calcMethod.OutputArguments = new PropertyState<Argument[]>(calcMethod);
            calcMethod.OutputArguments.Create(
                SystemContext,
                new NodeId("CalculatePressureVolumeMass_OutputArguments", NamespaceIndex),
                new QualifiedName(BrowseNames.OutputArguments),
                null,
                false);
            calcMethod.OutputArguments.Value =
            [
                new Argument { Name = "result", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar }
            ];
            calcMethod.Executable = true;
            calcMethod.UserExecutable = true;
            calcMethod.OnCallMethod = OnCalculatePressureVolumeMass;
            root.AddChild(calcMethod);
            AddPredefinedNode(SystemContext, calcMethod);
        }
    }

    // Exposes DensityDataType so clients can resolve the structure behind ns=2;s=Density:
    //  - DataType node (subtype of Structure) with a StructureDefinition (OPC UA 1.04+ clients)
    //  - Default Binary encoding node linked via HasEncoding
    //  - OPC Binary type dictionary + DataTypeDescription linked via HasDescription (pre-1.04 clients, e.g. Matrikon)
    private NodeId CreateDensityDataType(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        var typeId = new NodeId("DensityDataType", NamespaceIndex);
        var encodingId = new NodeId("DensityDataType_Encoding_DefaultBinary", NamespaceIndex);

        var dataType = new DataTypeState
        {
            NodeId = typeId,
            BrowseName = new QualifiedName("DensityDataType", NamespaceIndex),
            DisplayName = new LocalizedText("DensityDataType"),
            SuperTypeId = DataTypeIds.Structure,
            IsAbstract = false,
            DataTypeDefinition = new ExtensionObject(new StructureDefinition
            {
                DefaultEncodingId = encodingId,
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField { Name = "Volume", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Mass", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar }
                ]
            })
        };
        dataType.AddReference(ReferenceTypeIds.HasEncoding, false, encodingId);
        AddExternalReference(externalReferences, DataTypeIds.Structure, ReferenceTypeIds.HasSubtype, typeId);

        var encoding = new BaseObjectState(null)
        {
            NodeId = encodingId,
            BrowseName = new QualifiedName(BrowseNames.DefaultBinary, 0),
            DisplayName = new LocalizedText(BrowseNames.DefaultBinary),
            TypeDefinitionId = ObjectTypeIds.DataTypeEncodingType
        };
        encoding.AddReference(ReferenceTypeIds.HasEncoding, true, typeId);

        var schema =
            $"""
            <opc:TypeDictionary xmlns:opc="http://opcfoundation.org/BinarySchema/" xmlns:ua="http://opcfoundation.org/UA/" xmlns:tns="{NamespaceUris.First()}" DefaultByteOrder="LittleEndian" TargetNamespace="{NamespaceUris.First()}">
              <opc:Import Namespace="http://opcfoundation.org/UA/" />
              <opc:StructuredType Name="DensityDataType" BaseType="ua:ExtensionObject">
                <opc:Field Name="Volume" TypeName="opc:Double" />
                <opc:Field Name="Mass" TypeName="opc:Double" />
              </opc:StructuredType>
            </opc:TypeDictionary>
            """;

        var dictionary = new BaseDataVariableState<byte[]>(null)
        {
            NodeId = new NodeId("PLCSimulator_BinarySchema", NamespaceIndex),
            BrowseName = new QualifiedName("PLCSimulator", NamespaceIndex),
            DisplayName = new LocalizedText("PLCSimulator"),
            TypeDefinitionId = VariableTypeIds.DataTypeDictionaryType,
            ReferenceTypeId = ReferenceTypeIds.HasComponent,
            DataType = DataTypeIds.ByteString,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            Value = System.Text.Encoding.UTF8.GetBytes(schema),
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow
        };
        dictionary.AddReference(ReferenceTypeIds.HasComponent, true, ObjectIds.OPCBinarySchema_TypeSystem);
        AddExternalReference(externalReferences, ObjectIds.OPCBinarySchema_TypeSystem, ReferenceTypeIds.HasComponent, dictionary.NodeId);

        var namespaceUri = new PropertyState<string>(dictionary)
        {
            NodeId = new NodeId("PLCSimulator_BinarySchema_NamespaceUri", NamespaceIndex),
            BrowseName = new QualifiedName(BrowseNames.NamespaceUri, 0),
            DisplayName = new LocalizedText(BrowseNames.NamespaceUri),
            TypeDefinitionId = VariableTypeIds.PropertyType,
            ReferenceTypeId = ReferenceTypeIds.HasProperty,
            DataType = DataTypeIds.String,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            Value = NamespaceUris.First()
        };
        dictionary.AddChild(namespaceUri);

        var description = new BaseDataVariableState<string>(dictionary)
        {
            NodeId = new NodeId("PLCSimulator_BinarySchema_DensityDataType", NamespaceIndex),
            BrowseName = new QualifiedName("DensityDataType", NamespaceIndex),
            DisplayName = new LocalizedText("DensityDataType"),
            TypeDefinitionId = VariableTypeIds.DataTypeDescriptionType,
            ReferenceTypeId = ReferenceTypeIds.HasComponent,
            DataType = DataTypeIds.String,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            Value = "DensityDataType"
        };
        dictionary.AddChild(description);

        encoding.AddReference(ReferenceTypeIds.HasDescription, false, description.NodeId);
        description.AddReference(ReferenceTypeIds.HasDescription, true, encodingId);

        AddPredefinedNode(SystemContext, dataType);
        AddPredefinedNode(SystemContext, encoding);
        AddPredefinedNode(SystemContext, dictionary);

        return typeId;
    }

    private static void AddExternalReference(
        IDictionary<NodeId, IList<IReference>> externalReferences,
        NodeId sourceId,
        NodeId referenceTypeId,
        NodeId targetId)
    {
        if (!externalReferences.TryGetValue(sourceId, out var references))
            externalReferences[sourceId] = references = new List<IReference>();
        references.Add(new NodeStateReference(referenceTypeId, false, targetId));
    }

    public void UpdatePressure(double value)
    {
        lock (Lock)
        {
            _pressure.Value = value;
            _pressure.Timestamp = DateTime.UtcNow;
            _pressure.StatusCode = StatusCodes.Good;
            _pressure.ClearChangeMasks(SystemContext, false);
        }
    }

    public void UpdateMsg(string value)
    {
        lock (Lock)
        {
            _msg.Value = value;
            _msg.Timestamp = DateTime.UtcNow;
            _msg.StatusCode = StatusCodes.Good;
            _msg.ClearChangeMasks(SystemContext, false);
        }
    }

    public void UpdateDensity(double volume, double mass)
    {
        lock (Lock)
        {
            _density.Value = new ExtensionObject(DensityData.BinaryEncodingId, new DensityData { Volume = volume, Mass = mass });
            _density.Timestamp = DateTime.UtcNow;
            _density.StatusCode = StatusCodes.Good;
            _density.ClearChangeMasks(SystemContext, false);
        }
    }

    private ServiceResult OnCalculatePressureVolumeMass(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        var temperature = (double)inputArguments[0];
        outputArguments[0] = CalculatePressureVolumeMass(temperature);
        return ServiceResult.Good;
    }

    public double CalculatePressureVolumeMass(double temperature)
    {
        lock (Lock)
        {
            var d = (_density.Value?.Body as DensityData) ?? new DensityData();
            return _pressure.Value * d.Volume * d.Mass;
        }
    }
}
