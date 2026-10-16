using Opc.Ua;
using Opc.Ua.Server;

namespace OpcUaServer;

public class PlcNodeManager : CustomNodeManager2
{
    private BaseDataVariableState<double> _pressure = null!;
    private BaseDataVariableState<string> _msg = null!;
    private const int TankHistoryLength = 3;
    private BaseDataVariableState<ExtensionObject> _density = null!;
    private BaseDataVariableState<ExtensionObject> _tank = null!;
    private readonly Queue<DensityData> _tankHistory = new();

    public PlcNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, "http://plcsimulator.org/")
    {
        var builder = server.Factory.Builder;
        builder.AddType(DensityData.BinaryEncodingId, typeof(DensityData));
        builder.AddType(TankData.BinaryEncodingId, typeof(TankData));
        builder.Commit();
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
            root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));
            AddPredefinedNode(SystemContext, root);

            _pressure = new BaseDataVariableState<double>.Implementation<VariantBuilder>(root);
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

            _msg = new BaseDataVariableState<string>.Implementation<VariantBuilder>(root);
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

            var densityType = CreateStructureDataType(externalReferences, "DensityDataType",
                new StructureField { Name = "Volume", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
                new StructureField { Name = "Mass", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar });
            var densityTypeId = densityType.DataType.NodeId;
            var tankType = CreateStructureDataType(externalReferences, "TankDataType",
                new StructureField { Name = "Name", DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar },
                new StructureField { Name = "Density", DataType = densityTypeId, ValueRank = ValueRanks.Scalar },
                new StructureField { Name = "History", DataType = densityTypeId, ValueRank = ValueRanks.OneDimension },
                new StructureField { Name = "Readings", DataType = DataTypeIds.Double, ValueRank = ValueRanks.OneDimension });
            CreateBinaryTypeDictionary(externalReferences, densityType, tankType);

            // Density variable — single node whose value is a DensityData structure
            _density = new BaseDataVariableState<ExtensionObject>.Implementation<VariantBuilder>(root);
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

            // Tank variable — a TankData structure nesting a DensityData and an array of them
            _tank = new BaseDataVariableState<ExtensionObject>.Implementation<VariantBuilder>(root);
            _tank.Create(
                SystemContext,
                new NodeId("Tank", NamespaceIndex),
                new QualifiedName("Tank", NamespaceIndex),
                new LocalizedText("Tank"),
                false);
            _tank.DataType = tankType.DataType.NodeId;
            _tank.ValueRank = ValueRanks.Scalar;
            _tank.AccessLevel = AccessLevels.CurrentRead;
            _tank.UserAccessLevel = AccessLevels.CurrentRead;
            _tank.Value = new ExtensionObject(TankData.BinaryEncodingId, new TankData { Name = "Tank1" });
            _tank.StatusCode = StatusCodes.Good;
            _tank.Timestamp = DateTime.UtcNow;
            root.AddChild(_tank);
            AddPredefinedNode(SystemContext, _tank);

            CreateSetpoints(root, densityTypeId, tankType.DataType.NodeId);

            var calcMethod = new MethodState(root);
            calcMethod.Create(
                SystemContext,
                new NodeId("CalculatePressureVolumeMass", NamespaceIndex),
                new QualifiedName("CalculatePressureVolumeMass", NamespaceIndex),
                new LocalizedText("CalculatePressureVolumeMass"),
                false);
            calcMethod.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            calcMethod.InputArguments = new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(calcMethod);
            calcMethod.InputArguments.Create(
                SystemContext,
                new NodeId("CalculatePressureVolumeMass_InputArguments", NamespaceIndex),
                new QualifiedName(BrowseNames.InputArguments),
                LocalizedText.Null,
                false);
            calcMethod.InputArguments.Value =
            [
                new Argument { Name = "temperature", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar }
            ];
            calcMethod.OutputArguments = new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(calcMethod);
            calcMethod.OutputArguments.Create(
                SystemContext,
                new NodeId("CalculatePressureVolumeMass_OutputArguments", NamespaceIndex),
                new QualifiedName(BrowseNames.OutputArguments),
                LocalizedText.Null,
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

    // Writable nodes the simulation loop never touches, one per kind of value a client can write, plus nodes
    // that must not be writable (read-only, matrix, abstract type, a type with no natural form).
    private void CreateSetpoints(FolderState root, NodeId densityTypeId, NodeId tankTypeId)
    {
        var folder = new FolderState(root) { ReferenceTypeId = ReferenceTypeIds.Organizes };
        folder.Create(
            SystemContext,
            new NodeId("Setpoints", NamespaceIndex),
            new QualifiedName("Setpoints", NamespaceIndex),
            new LocalizedText("Setpoints"),
            false);
        root.AddChild(folder);

        var density = new DensityData { Volume = 1.0, Mass = 2.0 };
        AddSetpoint(folder, "Double", DataTypeIds.Double, ValueRanks.Scalar, new Variant(0.0));
        AddSetpoint(folder, "Int16", DataTypeIds.Int16, ValueRanks.Scalar, new Variant((short)0));
        AddSetpoint(folder, "Boolean", DataTypeIds.Boolean, ValueRanks.Scalar, new Variant(false));
        AddSetpoint(folder, "String", DataTypeIds.String, ValueRanks.Scalar, new Variant(string.Empty));
        AddSetpoint(folder, "DateTime", DataTypeIds.DateTime, ValueRanks.Scalar, new Variant(new DateTimeUtc(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))));
        AddSetpoint(folder, "DoubleArray", DataTypeIds.Double, ValueRanks.OneDimension, new Variant((ArrayOf<double>)new[] { 1.0, 2.0 }));
        AddSetpoint(folder, "Int32Array", DataTypeIds.Int32, ValueRanks.OneDimension, new Variant((ArrayOf<int>)new[] { 1, 2 }));
        AddSetpoint(folder, "Density", densityTypeId, ValueRanks.Scalar, new Variant(new ExtensionObject(DensityData.BinaryEncodingId, density)));
        AddSetpoint(folder, "DensityArray", densityTypeId, ValueRanks.OneDimension,
            new Variant((ArrayOf<ExtensionObject>)new[] { new ExtensionObject(DensityData.BinaryEncodingId, density) }));
        AddSetpoint(folder, "Tank", tankTypeId, ValueRanks.Scalar, new Variant(new ExtensionObject(TankData.BinaryEncodingId, new TankData { Name = "Setpoint" })));
        var percent = AddSetpoint(folder, "Percent", DataTypeIds.Double, ValueRanks.Scalar, new Variant(0.0));
        percent.OnSimpleWriteValue = OnWritePercent;
        AddSetpoint(folder, "ReadOnly", DataTypeIds.Double, ValueRanks.Scalar, new Variant(0.0), AccessLevels.CurrentRead, AccessLevels.CurrentRead);
        AddSetpoint(folder, "UserReadOnly", DataTypeIds.Double, ValueRanks.Scalar, new Variant(0.0), AccessLevels.CurrentReadOrWrite, AccessLevels.CurrentRead);
        var matrix = AddSetpoint(folder, "Matrix", DataTypeIds.Double, ValueRanks.TwoDimensions, new Variant(((ArrayOf<double>)new double[4]).ToMatrix([2, 2])));
        matrix.ArrayDimensions = (ArrayOf<uint>)new uint[] { 2, 2 };
        AddSetpoint(folder, "Number", DataTypeIds.Number, ValueRanks.Scalar, new Variant(0.0));
        AddSetpoint(folder, "Text", DataTypeIds.LocalizedText, ValueRanks.Scalar, new Variant(new LocalizedText("en", "Setpoint")));

        AddPredefinedNode(SystemContext, folder);
    }

    private BaseDataVariableState AddSetpoint(FolderState folder, string name, NodeId dataType, int valueRank, Variant value,
        byte accessLevel = AccessLevels.CurrentReadOrWrite, byte userAccessLevel = AccessLevels.CurrentReadOrWrite)
    {
        var variable = new BaseDataVariableState(folder);
        variable.Create(
            SystemContext,
            new NodeId($"Setpoints.{name}", NamespaceIndex),
            new QualifiedName(name, NamespaceIndex),
            new LocalizedText(name),
            false);
        variable.DataType = dataType;
        variable.ValueRank = valueRank;
        variable.AccessLevel = accessLevel;
        variable.UserAccessLevel = userAccessLevel;
        variable.WrappedValue = value;
        variable.StatusCode = StatusCodes.Good;
        variable.Timestamp = DateTime.UtcNow;
        folder.AddChild(variable);
        return variable;
    }

    // Setpoints.Percent only takes 0 to 100, so a client can see a server refuse a write.
    private static ServiceResult OnWritePercent(ISystemContext context, NodeState node, ref Variant value) =>
        value.TryGetValue(out double percent) && percent is >= 0 and <= 100 ? ServiceResult.Good : StatusCodes.BadOutOfRange;

    private sealed record StructureDataType(DataTypeState DataType, BaseObjectState Encoding);

    // Exposes a structure DataType so clients can resolve the structure behind a variable:
    //  - DataType node (subtype of Structure) with a StructureDefinition (OPC UA 1.04+ clients)
    //  - Default Binary encoding node linked via HasEncoding
    // The nodes are added by CreateBinaryTypeDictionary, once the encodings are linked to their descriptions.
    private StructureDataType CreateStructureDataType(IDictionary<NodeId, IList<IReference>> externalReferences, string typeName, params StructureField[] fields)
    {
        var typeId = new NodeId(typeName, NamespaceIndex);
        var encodingId = new NodeId($"{typeName}_Encoding_DefaultBinary", NamespaceIndex);

        var dataType = new DataTypeState
        {
            NodeId = typeId,
            BrowseName = new QualifiedName(typeName, NamespaceIndex),
            DisplayName = new LocalizedText(typeName),
            SuperTypeId = DataTypeIds.Structure,
            IsAbstract = false,
            DataTypeDefinition = new ExtensionObject(new StructureDefinition
            {
                DefaultEncodingId = encodingId,
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = [.. fields]
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
        return new StructureDataType(dataType, encoding);
    }

    // The OPC Binary type dictionary + a DataTypeDescription per type linked via HasDescription, for pre-1.04
    // clients (e.g. Matrikon). A namespace has one dictionary holding all of its structures.
    private void CreateBinaryTypeDictionary(IDictionary<NodeId, IList<IReference>> externalReferences, params StructureDataType[] types)
    {
        var schema =
            $"""
            <opc:TypeDictionary xmlns:opc="http://opcfoundation.org/BinarySchema/" xmlns:ua="http://opcfoundation.org/UA/" xmlns:tns="{NamespaceUris.First()}" DefaultByteOrder="LittleEndian" TargetNamespace="{NamespaceUris.First()}">
              <opc:Import Namespace="http://opcfoundation.org/UA/" />
              <opc:StructuredType Name="DensityDataType" BaseType="ua:ExtensionObject">
                <opc:Field Name="Volume" TypeName="opc:Double" />
                <opc:Field Name="Mass" TypeName="opc:Double" />
              </opc:StructuredType>
              <opc:StructuredType Name="TankDataType" BaseType="ua:ExtensionObject">
                <opc:Field Name="Name" TypeName="opc:String" />
                <opc:Field Name="Density" TypeName="tns:DensityDataType" />
                <opc:Field Name="NoOfHistory" TypeName="opc:Int32" />
                <opc:Field Name="History" TypeName="tns:DensityDataType" LengthField="NoOfHistory" />
                <opc:Field Name="NoOfReadings" TypeName="opc:Int32" />
                <opc:Field Name="Readings" TypeName="opc:Double" LengthField="NoOfReadings" />
              </opc:StructuredType>
            </opc:TypeDictionary>
            """;

        var dictionary = new BaseDataVariableState<ByteString>.Implementation<VariantBuilder>(null)
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
            Value = System.Text.Encoding.UTF8.GetBytes(schema).ToByteString(),
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow
        };
        dictionary.AddReference(ReferenceTypeIds.HasComponent, true, ObjectIds.OPCBinarySchema_TypeSystem);
        AddExternalReference(externalReferences, ObjectIds.OPCBinarySchema_TypeSystem, ReferenceTypeIds.HasComponent, dictionary.NodeId);

        var namespaceUri = new PropertyState<string>.Implementation<VariantBuilder>(dictionary)
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

        foreach (var type in types)
        {
            var typeName = type.DataType.BrowseName.Name ?? string.Empty;
            var description = new BaseDataVariableState<string>.Implementation<VariantBuilder>(dictionary)
            {
                NodeId = new NodeId($"PLCSimulator_BinarySchema_{typeName}", NamespaceIndex),
                BrowseName = new QualifiedName(typeName, NamespaceIndex),
                DisplayName = new LocalizedText(typeName),
                TypeDefinitionId = VariableTypeIds.DataTypeDescriptionType,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = typeName
            };
            dictionary.AddChild(description);

            type.Encoding.AddReference(ReferenceTypeIds.HasDescription, false, description.NodeId);
            description.AddReference(ReferenceTypeIds.HasDescription, true, type.Encoding.NodeId);

            AddPredefinedNode(SystemContext, type.DataType);
            AddPredefinedNode(SystemContext, type.Encoding);
        }
        AddPredefinedNode(SystemContext, dictionary);
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

    public void UpdateTank(double pressure, double volume, double mass)
    {
        lock (Lock)
        {
            var current = new DensityData { Volume = volume, Mass = mass };
            _tankHistory.Enqueue(current);
            while (_tankHistory.Count > TankHistoryLength) _tankHistory.Dequeue();
            _tank.Value = new ExtensionObject(TankData.BinaryEncodingId, new TankData
            {
                Name = "Tank1",
                Density = current,
                History = [.. _tankHistory],
                Readings = [pressure, volume, mass]
            });
            _tank.Timestamp = DateTime.UtcNow;
            _tank.StatusCode = StatusCodes.Good;
            _tank.ClearChangeMasks(SystemContext, false);
        }
    }

    private ServiceResult OnCalculatePressureVolumeMass(
        ISystemContext context,
        MethodState method,
        ArrayOf<Variant> inputArguments,
        List<Variant> outputArguments)
    {
        var temperature = (double)inputArguments[0];
        outputArguments[0] = CalculatePressureVolumeMass(temperature);
        return ServiceResult.Good;
    }

    public double CalculatePressureVolumeMass(double temperature)
    {
        lock (Lock)
        {
            var d = _density.Value.TryGetValue(out DensityData? density) && density != null ? density : new DensityData();
            return _pressure.Value * d.Volume * d.Mass;
        }
    }
}
