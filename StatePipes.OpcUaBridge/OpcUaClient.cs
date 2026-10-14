using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// The OPC UA half of the bridge: owns one session, finds every readable variable and reads them on demand.
    ///
    /// <para>Discovery walks the hierarchical references down from the Objects folder, which is how a generic
    /// client sees "everything on the server". Namespace 0 and the stack's Diagnostics namespace hold the server's
    /// own diagnostics and type system rather than plant data, so their nodes are skipped unless <see cref="BridgeSettings.IncludeNamespaceZero"/>
    /// is set; they are still browsed through, because user nodes often hang beneath them.
    /// <see cref="BridgeSettings.NodeFilter"/> narrows the set further on servers too large to describe in one
    /// self-description message.</para>
    /// </summary>
    internal sealed class OpcUaClient : IAsyncDisposable
    {
        private const uint SessionTimeoutMilliseconds = 60_000;
        private const int BrowseBatchSize = 100;
        private const int ReadBatchSize = 500;
        private const string DiagnosticsNamespaceUri = "http://opcfoundation.org/UA/Diagnostics";
        private readonly BridgeSettings _settings;
        private readonly ITelemetryContext _telemetry;
        private ApplicationConfiguration? _configuration;
        private ISession? _session;
        private volatile bool _keepAliveFailed;

        public OpcUaClient(BridgeSettings settings)
        {
            _settings = settings;
            _telemetry = DefaultTelemetry.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        }

        public bool IsConnected => _session is { Connected: true } && !_keepAliveFailed;

        public async Task ConnectAsync(CancellationToken ct)
        {
            await DisconnectAsync();
            _configuration ??= await CreateConfigurationAsync(ct);
            var endpointDescription = await CoreClientUtils.SelectEndpointAsync(_configuration, _settings.OpcUaEndpoint, _settings.UseSecurity, _telemetry, ct)
                ?? throw new InvalidOperationException($"No endpoint found at {_settings.OpcUaEndpoint}");
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, EndpointConfiguration.Create(_configuration));
            var session = await new DefaultSessionFactory(_telemetry).CreateAsync(_configuration, endpoint, false, false,
                _settings.ApplicationName, SessionTimeoutMilliseconds, CreateIdentity(), default, ct);
            _keepAliveFailed = false;
            session.KeepAlive += OnKeepAlive;
            _session = session;
            Log?.LogInfo($"Connected to OPC UA server {endpointDescription.EndpointUrl} [{endpointDescription.SecurityMode} {SecurityPolicies.Default.GetDisplayName(endpointDescription.SecurityPolicyUri ?? string.Empty) ?? endpointDescription.SecurityPolicyUri}]");
        }

        public async Task DisconnectAsync()
        {
            var session = _session;
            _session = null;
            if (session == null) return;
            session.KeepAlive -= OnKeepAlive;
            try { await session.CloseAsync(); } catch { }
            session.Dispose();
        }

        /// <summary>
        /// Finds every variable the session is allowed to read. The returned items are named but not yet
        /// typed by anything beyond the server's declared DataType and ValueRank.
        /// </summary>
        public async Task<IReadOnlyList<OpcUaDataItem>> DiscoverAsync(CancellationToken ct)
        {
            var session = _session ?? throw new InvalidOperationException("Not connected to an OPC UA server");
            var variables = await BrowseVariablesAsync(session, ct);
            var readable = await ReadVariableAttributesAsync(session, variables, ct);
            var names = OpcUaNaming.AssignUniqueNames(_settings.BusConfig.ExchangeNamePrefix, readable.Keys, message => Log?.LogWarning(message));
            return [.. readable
                .Select(kv => new OpcUaDataItem(kv.Key, kv.Key.ToString(), names[kv.Key], kv.Value.ValueType, kv.Value.Structure, kv.Value.IsArray))
                .OrderBy(item => item.CommandTypeFullName, StringComparer.Ordinal)];
        }

        public async Task<IReadOnlyList<DataValue>> ReadValuesAsync(IReadOnlyList<NodeId> nodeIds, CancellationToken ct)
        {
            var session = _session;
            if (session == null || !IsConnected) return [.. nodeIds.Select(_ => DataValue.FromStatusCode(StatusCodes.BadNotConnected))];
            List<ReadValueId> toRead = [.. nodeIds.Select(nodeId => new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value })];
            var response = await session.ReadAsync(null, 0, TimestampsToReturn.Both, toRead, ct);
            var results = response.Results.ToList();
            ClientBase.ValidateResponse(results, toRead);
            return results;
        }

        private async Task<ApplicationConfiguration> CreateConfigurationAsync(CancellationToken ct)
        {
            var pki = _settings.PkiPath;
            ApplicationConfiguration configuration = new(_telemetry)
            {
                ApplicationName = _settings.ApplicationName,
                ApplicationUri = $"urn:{Utils.GetHostName()}:{_settings.ApplicationName}",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pki, "own"),
                        SubjectName = $"CN={_settings.ApplicationName}, O=StatePipes, DC={Utils.GetHostName()}"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(pki, "issuer") },
                    TrustedPeerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(pki, "trusted") },
                    RejectedCertificateStore = new CertificateStoreIdentifier(Path.Combine(pki, "rejected")),
                    AutoAcceptUntrustedCertificates = _settings.AutoAcceptUntrustedCertificates,
                    AddAppCertToTrustedStore = true
                },
                TransportQuotas = new TransportQuotas { OperationTimeout = 15_000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = (int)SessionTimeoutMilliseconds }
            };
            await configuration.ValidateAsync(ApplicationType.Client, ct);
            ApplicationInstance application = new(configuration, _telemetry);
            if (!await application.CheckApplicationInstanceCertificatesAsync(false, null, ct))
                throw new InvalidOperationException("The OPC UA application instance certificate is invalid");
            // CertificateManager is only populated by CheckApplicationInstanceCertificatesAsync
            configuration.CertificateManager.AcceptError = OnCertificateError;
            return configuration;
        }

        private bool OnCertificateError(Certificate certificate, ServiceResult error)
        {
            if (error.StatusCode != StatusCodes.BadCertificateUntrusted) return false;
            if (_settings.AutoAcceptUntrustedCertificates)
            {
                Log?.LogInfo($"Auto-accepted untrusted OPC UA server certificate {certificate.Subject}");
                return true;
            }
            Log?.LogError($"Rejected untrusted OPC UA server certificate {certificate.Subject}. Copy it from {Path.Combine(_settings.PkiPath, "rejected")} to {Path.Combine(_settings.PkiPath, "trusted")} or set OPCUA_AUTO_ACCEPT_UNTRUSTED=true");
            return false;
        }

        private IUserIdentity CreateIdentity() => string.IsNullOrEmpty(_settings.UserName)
            ? new UserIdentity()
            : new UserIdentity(_settings.UserName, System.Text.Encoding.UTF8.GetBytes(_settings.Password ?? string.Empty));

        private void OnKeepAlive(ISession session, KeepAliveEventArgs e)
        {
            if (!ServiceResult.IsBad(e.Status)) return;
            if (!_keepAliveFailed) Log?.LogError($"OPC UA keep alive failed: {e.Status}");
            _keepAliveFailed = true;
        }

        /// <summary>Breadth-first walk of the hierarchy below Objects, returning every Variable node found.</summary>
        private static async Task<List<NodeId>> BrowseVariablesAsync(ISession session, CancellationToken ct)
        {
            List<NodeId> variables = [];
            HashSet<NodeId> visited = [ObjectIds.ObjectsFolder];
            Queue<NodeId> toBrowse = new([ObjectIds.ObjectsFolder]);
            while (toBrowse.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                List<BrowseDescription> batch = [];
                while (toBrowse.Count > 0 && batch.Count < BrowseBatchSize) batch.Add(CreateBrowseDescription(toBrowse.Dequeue()));
                foreach (var reference in await BrowseAllAsync(session, batch, ct))
                {
                    if (reference.NodeId.IsAbsolute) continue; // lives on another server
                    var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                    if (nodeId.IsNull || !visited.Add(nodeId)) continue;
                    if (reference.NodeClass == NodeClass.Variable) variables.Add(nodeId);
                    toBrowse.Enqueue(nodeId); // variables can have child variables (properties, structure fields)
                }
            }
            return variables;
        }

        private static BrowseDescription CreateBrowseDescription(NodeId nodeId) => new()
        {
            NodeId = nodeId,
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
            ResultMask = (uint)(BrowseResultMask.NodeClass | BrowseResultMask.ReferenceTypeId)
        };

        /// <summary>Browses a batch, following continuation points until every reference has been returned.</summary>
        private static async Task<List<ReferenceDescription>> BrowseAllAsync(ISession session, List<BrowseDescription> batch, CancellationToken ct)
        {
            List<ReferenceDescription> references = [];
            var response = await session.BrowseAsync(null, null, 0, batch, ct);
            var results = response.Results;
            while (true)
            {
                List<ByteString> continuationPoints = [];
                foreach (var result in results)
                {
                    if (StatusCode.IsBad(result.StatusCode)) continue;
                    references.AddRange(result.References);
                    if (result.ContinuationPoint is { Length: > 0 }) continuationPoints.Add(result.ContinuationPoint);
                }
                if (continuationPoints.Count == 0) return references;
                var next = await session.BrowseNextAsync(null, false, continuationPoints, ct);
                results = next.Results;
            }
        }

        /// <summary>
        /// Reads access level, data type and value rank for each variable, keeping only those this session may
        /// read and pairing each with the CLR type its value will be published as, or, for a structure the
        /// bridge can decode, with the structure's layout.
        /// </summary>
        private async Task<Dictionary<NodeId, (Type ValueType, StructureLayout? Structure, bool IsArray)>> ReadVariableAttributesAsync(ISession session, List<NodeId> variables, CancellationToken ct)
        {
            Dictionary<NodeId, (Type, StructureLayout?, bool)> readable = [];
            Dictionary<NodeId, DataTypeInfo?> dataTypes = [];
            StructureLayoutBuilder layouts = new(_settings.BusConfig.ExchangeNamePrefix, async (dataTypeId, token) =>
            {
                if (!dataTypes.TryGetValue(dataTypeId, out var info)) dataTypes[dataTypeId] = info = await DescribeDataTypeAsync(session, dataTypeId, token);
                return info;
            });
            uint[] attributes = [Attributes.UserAccessLevel, Attributes.DataType, Attributes.ValueRank];
            foreach (var chunk in variables.Where(nodeId => IsIncluded(nodeId, session.NamespaceUris)).Chunk(ReadBatchSize / attributes.Length))
            {
                ct.ThrowIfCancellationRequested();
                List<ReadValueId> toRead = [.. chunk.SelectMany(nodeId => attributes.Select(attributeId => new ReadValueId { NodeId = nodeId, AttributeId = attributeId }))];
                var response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither, toRead, ct);
                var results = response.Results;
                for (var i = 0; i < chunk.Length; i++)
                {
                    var accessLevel = results[i * 3];
                    var dataType = results[i * 3 + 1];
                    var valueRank = results[i * 3 + 2];
                    if (StatusCode.IsBad(accessLevel.StatusCode) || !accessLevel.WrappedValue.TryGetValue(out byte level) || (level & AccessLevels.CurrentRead) == 0) continue;
                    var dataTypeId = dataType.WrappedValue.TryGetValue(out NodeId id) ? id : NodeId.Null;
                    var builtInType = !dataTypeId.IsNull ? await TypeInfo.GetBuiltInTypeAsync(dataTypeId, session.TypeTree, ct) : BuiltInType.Null;
                    var rank = valueRank.WrappedValue.TryGetValue(out int r) ? r : ValueRanks.Any;
                    var valueType = OpcUaValueMapper.MapToClrType(builtInType, rank);
                    var structure = await BuildStructureLayoutAsync(layouts, chunk[i], dataTypeId, builtInType, rank, ct);
                    readable[chunk[i]] = (valueType, structure, rank == ValueRanks.OneDimension);
                }
            }
            return readable;
        }

        /// <summary>
        /// The layout of a variable holding a structure or a one-dimensional array of one, or null when its value
        /// is not a structure or not one the bridge can decode -- then it travels as a string, as before.
        /// </summary>
        private static async Task<StructureLayout?> BuildStructureLayoutAsync(StructureLayoutBuilder layouts, NodeId variable, NodeId dataTypeId, BuiltInType builtInType, int rank, CancellationToken ct)
        {
            if (builtInType != BuiltInType.ExtensionObject || dataTypeId.IsNull || dataTypeId == DataTypeIds.Structure) return null;
            if (rank != ValueRanks.Scalar && rank != ValueRanks.OneDimension) return null;
            var layout = await layouts.BuildAsync(dataTypeId, ct);
            if (layout == null) Log?.LogWarning($"{variable} holds structure {dataTypeId}, which the bridge cannot decode; its value is published as a string");
            return layout;
        }

        /// <summary>
        /// Reads what a DataType's layout depends on. The definition comes from the DataTypeDefinition attribute
        /// (OPC UA 1.04); a server without it describes no structures and they travel as strings.
        /// </summary>
        private static async Task<DataTypeInfo?> DescribeDataTypeAsync(ISession session, NodeId dataTypeId, CancellationToken ct)
        {
            List<ReadValueId> toRead =
            [
                new ReadValueId { NodeId = dataTypeId, AttributeId = Attributes.IsAbstract },
                new ReadValueId { NodeId = dataTypeId, AttributeId = Attributes.DataTypeDefinition }
            ];
            var response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither, toRead, ct);
            var isAbstract = response.Results[0];
            if (StatusCode.IsBad(isAbstract.StatusCode)) return null;
            StructureDefinition? definition = response.Results[1].WrappedValue.GetStructure<StructureDefinition>();
            var builtInType = await TypeInfo.GetBuiltInTypeAsync(dataTypeId, session.TypeTree, ct);
            return new DataTypeInfo(builtInType, isAbstract.WrappedValue.TryGetValue(out bool abstractType) && abstractType, definition);
        }

        private bool IsIncluded(NodeId nodeId, NamespaceTable namespaceUris) => (_settings.IncludeNamespaceZero || !IsServerOwnNamespace(nodeId.NamespaceIndex, namespaceUris))
            && (_settings.NodeFilter?.IsMatch(nodeId.ToString()) ?? true);

        /// <summary>
        /// Namespace 0 and the stack's Diagnostics namespace hold the server's own diagnostics, conditions and
        /// type system rather than plant data.
        /// </summary>
        internal static bool IsServerOwnNamespace(ushort namespaceIndex, NamespaceTable namespaceUris) =>
            namespaceIndex == 0 || namespaceUris.GetString(namespaceIndex) == DiagnosticsNamespaceUri;

        public async ValueTask DisposeAsync() => await DisconnectAsync();
    }
}
