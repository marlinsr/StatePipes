using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// The OPC UA half of the bridge: owns one session, finds every readable variable and reads them on demand.
    ///
    /// <para>Discovery walks the hierarchical references down from the Objects folder, which is how a generic
    /// client sees "everything on the server". Namespace 0 holds the server's own diagnostics and type system
    /// rather than plant data, so its nodes are skipped unless <see cref="BridgeSettings.IncludeNamespaceZero"/>
    /// is set; they are still browsed through, because user nodes often hang beneath them.
    /// <see cref="BridgeSettings.NodeFilter"/> narrows the set further on servers too large to describe in one
    /// self-description message.</para>
    /// </summary>
    internal sealed class OpcUaClient : IAsyncDisposable
    {
        private const uint SessionTimeoutMilliseconds = 60_000;
        private const int BrowseBatchSize = 100;
        private const int ReadBatchSize = 500;
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
                _settings.ApplicationName, SessionTimeoutMilliseconds, CreateIdentity(), null, ct);
            _keepAliveFailed = false;
            session.KeepAlive += OnKeepAlive;
            _session = session;
            Log?.LogInfo($"Connected to OPC UA server {endpointDescription.EndpointUrl} [{endpointDescription.SecurityMode} {SecurityPolicies.GetDisplayName(endpointDescription.SecurityPolicyUri) ?? endpointDescription.SecurityPolicyUri}]");
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
            var names = OpcUaNaming.AssignUniqueNames(readable.Keys, message => Log?.LogError(message));
            return [.. readable
                .Select(kv => new OpcUaDataItem(kv.Key, kv.Key.ToString(), names[kv.Key], kv.Value))
                .OrderBy(item => item.MessageName, StringComparer.Ordinal)];
        }

        public async Task<IReadOnlyList<DataValue>> ReadValuesAsync(IReadOnlyList<NodeId> nodeIds, CancellationToken ct)
        {
            var session = _session;
            if (session == null || !IsConnected) return [.. nodeIds.Select(_ => new DataValue(StatusCodes.BadNotConnected))];
            ReadValueIdCollection toRead = [.. nodeIds.Select(nodeId => new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value })];
            var response = await session.ReadAsync(null, 0, TimestampsToReturn.Both, toRead, ct);
            ClientBase.ValidateResponse(response.Results, toRead);
            return response.Results;
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
            configuration.CertificateValidator.CertificateValidation += OnCertificateValidation;
            ApplicationInstance application = new(configuration, _telemetry);
            if (!await application.CheckApplicationInstanceCertificatesAsync(false, null, ct))
                throw new InvalidOperationException("The OPC UA application instance certificate is invalid");
            return configuration;
        }

        private void OnCertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode != StatusCodes.BadCertificateUntrusted) return;
            if (_settings.AutoAcceptUntrustedCertificates)
            {
                e.Accept = true;
                Log?.LogInfo($"Auto-accepted untrusted OPC UA server certificate {e.Certificate.Subject}");
            }
            else
            {
                Log?.LogError($"Rejected untrusted OPC UA server certificate {e.Certificate.Subject}. Copy it from {Path.Combine(_settings.PkiPath, "rejected")} to {Path.Combine(_settings.PkiPath, "trusted")} or set OPCUA_AUTO_ACCEPT_UNTRUSTED=true");
            }
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
                BrowseDescriptionCollection batch = [];
                while (toBrowse.Count > 0 && batch.Count < BrowseBatchSize) batch.Add(CreateBrowseDescription(toBrowse.Dequeue()));
                foreach (var reference in await BrowseAllAsync(session, batch, ct))
                {
                    if (reference.NodeId.IsAbsolute) continue; // lives on another server
                    var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                    if (nodeId == null || !visited.Add(nodeId)) continue;
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
        private static async Task<List<ReferenceDescription>> BrowseAllAsync(ISession session, BrowseDescriptionCollection batch, CancellationToken ct)
        {
            List<ReferenceDescription> references = [];
            var response = await session.BrowseAsync(null, null, 0, batch, ct);
            var results = response.Results;
            while (true)
            {
                ByteStringCollection continuationPoints = [];
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
        /// read and pairing each with the CLR type its value will be published as.
        /// </summary>
        private async Task<Dictionary<NodeId, Type>> ReadVariableAttributesAsync(ISession session, List<NodeId> variables, CancellationToken ct)
        {
            Dictionary<NodeId, Type> readable = [];
            uint[] attributes = [Attributes.UserAccessLevel, Attributes.DataType, Attributes.ValueRank];
            foreach (var chunk in variables.Where(IsIncluded).Chunk(ReadBatchSize / attributes.Length))
            {
                ct.ThrowIfCancellationRequested();
                ReadValueIdCollection toRead = [.. chunk.SelectMany(nodeId => attributes.Select(attributeId => new ReadValueId { NodeId = nodeId, AttributeId = attributeId }))];
                var response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither, toRead, ct);
                var results = response.Results;
                for (var i = 0; i < chunk.Length; i++)
                {
                    var accessLevel = results[i * 3];
                    var dataType = results[i * 3 + 1];
                    var valueRank = results[i * 3 + 2];
                    if (StatusCode.IsBad(accessLevel.StatusCode) || accessLevel.Value is not byte level || (level & AccessLevels.CurrentRead) == 0) continue;
                    var builtInType = dataType.Value is NodeId dataTypeId ? await TypeInfo.GetBuiltInTypeAsync(dataTypeId, session.TypeTree, ct) : BuiltInType.Null;
                    var rank = valueRank.Value is int r ? r : ValueRanks.Any;
                    readable[chunk[i]] = OpcUaValueMapper.MapToClrType(builtInType, rank);
                }
            }
            return readable;
        }

        private bool IsIncluded(NodeId nodeId) => (_settings.IncludeNamespaceZero || nodeId.NamespaceIndex != 0)
            && (_settings.NodeFilter?.IsMatch(nodeId.ToString()) ?? true);

        public async ValueTask DisposeAsync() => await DisconnectAsync();
    }
}
