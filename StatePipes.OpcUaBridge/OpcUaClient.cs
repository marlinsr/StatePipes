using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// The OPC UA half of the bridge: owns the session, finds every readable variable and reads them on demand,
    /// and calls methods over a second session of its own (see <see cref="CallAsync"/>).
    ///
    /// <para>Discovery walks the hierarchical references down from the Objects folder, which is how a generic
    /// client sees "everything on the server". Namespace 0 and the stack's Diagnostics namespace hold the server's
    /// own diagnostics and type system rather than plant data, so their nodes are skipped unless <see cref="BridgeSettings.IncludeNamespaceZero"/>
    /// is set; they are still browsed through, because user nodes often hang beneath them.
    /// <see cref="BridgeSettings.NodeFilter"/> narrows the set further on servers too large to describe in one
    /// self-description message.</para>
    /// </summary>
    internal sealed class OpcUaClient : IOpcUaSession, IAsyncDisposable
    {
        internal const uint SessionTimeoutMilliseconds = 60_000;
        private const int BrowseBatchSize = 100;
        private const int ReadBatchSize = 500;
        private const string DiagnosticsNamespaceUri = "http://opcfoundation.org/UA/Diagnostics";
        /// <summary>How long opening the call session may take; only the calls on it are untimed.</summary>
        private static readonly TimeSpan CallSessionOpenTimeout = TimeSpan.FromSeconds(15);
        private readonly BridgeSettings _settings;
        private readonly ITelemetryContext _telemetry;
        private ApplicationConfiguration? _configuration;
        private ISession? _session;
        private ConfiguredEndpoint? _endpoint;
        private ISession? _callSession;
        private readonly SemaphoreSlim _callSessionLock = new(1, 1);
        /// <summary>Cancelled, and replaced, by every disconnect, so a call session still opening gives up.</summary>
        private CancellationTokenSource _disconnected = new();
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
            var session = await CreateSessionAsync(endpoint, _settings.ApplicationName, ct);
            _keepAliveFailed = false;
            session.KeepAlive += OnKeepAlive;
            _endpoint = endpoint;
            _session = session;
            Log?.LogInfo($"Connected to OPC UA server {endpointDescription.EndpointUrl} [{endpointDescription.SecurityMode} {SecurityPolicies.Default.GetDisplayName(endpointDescription.SecurityPolicyUri ?? string.Empty) ?? endpointDescription.SecurityPolicyUri}]");
        }

        private Task<ISession> CreateSessionAsync(ConfiguredEndpoint endpoint, string sessionName, CancellationToken ct) =>
            new DefaultSessionFactory(_telemetry).CreateAsync(_configuration!, endpoint, false, false, sessionName, SessionTimeoutMilliseconds, CreateIdentity(), default, ct);

        public async Task DisconnectAsync()
        {
            var session = _session;
            _session = null;
            Interlocked.Exchange(ref _disconnected, new()).Cancel();
            CloseCallSession();
            if (session == null) return;
            session.KeepAlive -= OnKeepAlive;
            try { await session.CloseAsync(); } catch { }
            session.Dispose();
        }

        private void CloseCallSession() => DisposeCallSession(Interlocked.Exchange(ref _callSession, null));

        private static void DisposeCallSession(ISession? callSession)
        {
            // Dispose rather than close: closing waits for outstanding requests, and a running call must end now.
            try { callSession?.Dispose(); } catch { }
        }

        /// <summary>
        /// Finds every variable the session is allowed to read and every method it may execute. The returned
        /// items are named but not yet typed by anything beyond the server's declared DataType and ValueRank.
        /// </summary>
        public async Task<DiscoveredNodes> DiscoverAsync(CancellationToken ct)
        {
            var session = _session ?? throw new InvalidOperationException("Not connected to an OPC UA server");
            var (variables, methodPairs) = await BrowseAsync(session, ct);
            Dictionary<NodeId, DataTypeInfo?> dataTypes = [];
            StructureLayoutBuilder layouts = new(_settings.BusConfig.ExchangeNamePrefix, async (dataTypeId, token) =>
            {
                if (!dataTypes.TryGetValue(dataTypeId, out var info)) dataTypes[dataTypeId] = info = await DescribeDataTypeAsync(session, dataTypeId, token);
                return info;
            });
            var readable = await ReadVariableAttributesAsync(session, variables, layouts, ct);
            var names = OpcUaNaming.AssignUniqueNames(_settings.BusConfig.ExchangeNamePrefix, readable.Keys, message => Log?.LogWarning(message));
            List<OpcUaDataItem> items = [.. readable
                .Select(kv => new OpcUaDataItem(kv.Key, kv.Key.ToString(), names[kv.Key], kv.Value.ValueType, kv.Value.Structure, kv.Value.IsArray, kv.Value.BuiltInType, kv.Value.IsWritable))
                .OrderBy(item => item.CommandTypeFullName, StringComparer.Ordinal)];
            return new DiscoveredNodes(items, await DescribeMethodsAsync(session, methodPairs, layouts, ct));
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

        public async Task<IReadOnlyList<StatusCode>> WriteValuesAsync(IReadOnlyList<(NodeId NodeId, Variant Value)> values, CancellationToken ct)
        {
            var session = _session;
            if (session == null || !IsConnected) return [.. values.Select(_ => (StatusCode)StatusCodes.BadNotConnected)];
            List<WriteValue> toWrite = [.. values.Select(v => new WriteValue { NodeId = v.NodeId, AttributeId = Attributes.Value, Value = new DataValue(v.Value) })];
            var response = await session.WriteAsync(null, toWrite, ct);
            var results = response.Results.ToList();
            ClientBase.ValidateResponse(results, toWrite);
            return results;
        }

        /// <summary>
        /// Calls one method and returns the server's result, or BadNotConnected without a call when there is no
        /// session. Failures of the Call itself are thrown.
        ///
        /// <para>The call waits as long as the server takes. The stack cuts every request off at its session's
        /// OperationTimeout whatever the request's timeout hint says, so calls go over a second session of their
        /// own with no practical timeout, opened on the first call and closed with the main session. Everything
        /// else keeps the main session's timeout, so a dead connection is still noticed there.</para>
        /// </summary>
        public async Task<CallMethodResult> CallAsync(NodeId objectId, NodeId methodId, IReadOnlyList<Variant> inputs, CancellationToken ct)
        {
            var session = await GetCallSessionAsync(ct);
            if (session == null) return new CallMethodResult { StatusCode = StatusCodes.BadNotConnected };
            List<CallMethodRequest> toCall = [new CallMethodRequest { ObjectId = objectId, MethodId = methodId, InputArguments = [.. inputs] }];
            CallResponse response;
            try { response = await session.CallAsync(null, toCall, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The call session may be broken -- closed by the server, say -- while the main session is fine,
                // and nothing else would notice. Drop it so the next call opens a new one; this call is not retried.
                if (Interlocked.CompareExchange(ref _callSession, null, session) == session) DisposeCallSession(session);
                throw;
            }
            var results = response.Results.ToList();
            ClientBase.ValidateResponse(results, toCall);
            return results[0];
        }

        /// <summary>
        /// The open call session, opening one if needed, or null when the main session is not connected or is
        /// disconnected, or the call session could not be opened in <see cref="CallSessionOpenTimeout"/>.
        /// </summary>
        private async Task<ISession?> GetCallSessionAsync(CancellationToken ct)
        {
            await _callSessionLock.WaitAsync(ct);
            try
            {
                var disconnected = _disconnected.Token;
                var main = _session;
                if (main == null || !IsConnected || _endpoint == null) return null;
                if (_callSession is { Connected: true, KeepAliveStopped: false } open) return open;
                CloseCallSession();
                var configuration = EndpointConfiguration.Create(_configuration!);
                configuration.OperationTimeout = int.MaxValue;
                var endpoint = new ConfiguredEndpoint(null, _endpoint.Description, configuration);
                using var opening = CancellationTokenSource.CreateLinkedTokenSource(ct, disconnected);
                opening.CancelAfter(CallSessionOpenTimeout);
                ISession callSession;
                try { callSession = await CreateSessionAsync(endpoint, _settings.ApplicationName + " calls", opening.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Log?.LogWarning($"Could not open the OPC UA call session: {(disconnected.IsCancellationRequested ? "disconnected" : "timed out")}");
                    return null;
                }
                callSession.OperationTimeout = int.MaxValue;
                _callSession = callSession;
                // A disconnect cancels the token before closing the call session, so one that ran while this
                // session was opening is seen here, and this session closed, whatever the interleaving.
                if (disconnected.IsCancellationRequested || _session != main)
                {
                    CloseCallSession();
                    return null;
                }
                return callSession;
            }
            finally { _callSessionLock.Release(); }
        }

        /// <summary>The monitored items of the current session; a new session needs a new manager.</summary>
        public IMonitorSession CreateMonitorManager(Action<MonitorKey, DataValue> onNotification)
        {
            var session = _session;
            if (session == null || !IsConnected) throw new InvalidOperationException("Not connected to an OPC UA server");
            return new OpcUaMonitorManager(session, _telemetry, onNotification);
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

        /// <summary>
        /// Breadth-first walk of the hierarchy below Objects, returning every Variable node found and every
        /// (object, method) pair: a Method referenced from an Object. Methods are not walked into, and a method node
        /// referenced from several objects is a pair with each.
        /// </summary>
        private static async Task<(List<NodeId> Variables, List<(NodeId ObjectId, NodeId MethodId)> Methods)> BrowseAsync(ISession session, CancellationToken ct)
        {
            List<NodeId> variables = [];
            List<(NodeId, NodeId)> methods = [];
            HashSet<NodeId> visited = [ObjectIds.ObjectsFolder];
            Dictionary<NodeId, NodeClass> nodeClasses = new() { [ObjectIds.ObjectsFolder] = NodeClass.Object };
            Queue<NodeId> toBrowse = new([ObjectIds.ObjectsFolder]);
            while (toBrowse.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                List<BrowseDescription> batch = [];
                while (toBrowse.Count > 0 && batch.Count < BrowseBatchSize) batch.Add(CreateBrowseDescription(toBrowse.Dequeue()));
                foreach (var (source, reference) in await BrowseAllAsync(session, batch, ct))
                {
                    if (reference.NodeId.IsAbsolute) continue; // lives on another server
                    var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                    if (nodeId.IsNull) continue;
                    if (reference.NodeClass == NodeClass.Method)
                    {
                        // Recorded before the visited check: a method shared by two objects is reached twice.
                        if (nodeClasses.GetValueOrDefault(source) == NodeClass.Object) methods.Add((source, nodeId));
                        continue;
                    }
                    if (!visited.Add(nodeId)) continue;
                    nodeClasses[nodeId] = reference.NodeClass;
                    if (reference.NodeClass == NodeClass.Variable) variables.Add(nodeId);
                    toBrowse.Enqueue(nodeId); // variables can have child variables (properties, structure fields)
                }
            }
            return (variables, methods);
        }

        private static BrowseDescription CreateBrowseDescription(NodeId nodeId) => new()
        {
            NodeId = nodeId,
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.Method),
            ResultMask = (uint)(BrowseResultMask.NodeClass | BrowseResultMask.ReferenceTypeId)
        };

        /// <summary>
        /// Browses a batch, following continuation points until every reference has been returned, each paired
        /// with the node it was found on.
        /// </summary>
        private static async Task<List<(NodeId Source, ReferenceDescription Reference)>> BrowseAllAsync(ISession session, List<BrowseDescription> batch, CancellationToken ct)
        {
            List<(NodeId, ReferenceDescription)> references = [];
            var response = await session.BrowseAsync(null, null, 0, batch, ct);
            var results = response.Results;
            List<NodeId> sources = [.. batch.Select(description => description.NodeId)];
            while (true)
            {
                List<ByteString> continuationPoints = [];
                List<NodeId> continuedSources = [];
                for (var i = 0; i < results.Count; i++)
                {
                    var result = results[i];
                    if (StatusCode.IsBad(result.StatusCode)) continue;
                    foreach (var reference in result.References) references.Add((sources[i], reference));
                    if (result.ContinuationPoint is not { Length: > 0 }) continue;
                    continuationPoints.Add(result.ContinuationPoint);
                    continuedSources.Add(sources[i]);
                }
                if (continuationPoints.Count == 0) return references;
                var next = await session.BrowseNextAsync(null, false, continuationPoints, ct);
                results = next.Results;
                sources = continuedSources;
            }
        }

        /// <summary>
        /// Keeps the (object, method) pairs this session may call and the bridge can serve: not the server's own
        /// methods, passing <see cref="BridgeSettings.MethodFilter"/>, Executable and UserExecutable, and with
        /// every input argument of a shape a Set could write. Outputs are published as Get values are, so no output
        /// hides a method.
        /// </summary>
        private async Task<IReadOnlyList<OpcUaMethodItem>> DescribeMethodsAsync(ISession session, List<(NodeId ObjectId, NodeId MethodId)> pairs,
            StructureLayoutBuilder layouts, CancellationToken ct)
        {
            var candidates = pairs.Distinct()
                .Where(p => !IsServerOwnNamespace(p.MethodId.NamespaceIndex, session.NamespaceUris) && (_settings.MethodFilter?.IsMatch(p.MethodId.ToString()) ?? true))
                .ToList();
            var executable = await ReadExecutableAsync(session, [.. candidates.Select(p => p.MethodId).Distinct()], ct);
            var arguments = await ReadArgumentsAsync(session, executable, ct);
            Dictionary<NodeId, (IReadOnlyList<MethodArgument> Inputs, IReadOnlyList<MethodArgument> Outputs)> signatures = [];
            Dictionary<string, int> unexecutable = new(StringComparer.Ordinal);
            foreach (var methodId in executable)
            {
                var (inputArguments, outputArguments) = arguments[methodId];
                var inputs = await DescribeArgumentsAsync(session, layouts, methodId, inputArguments, MethodArgumentNaming.PropertyNames([.. inputArguments.Select(a => a.Name)], new HashSet<string>()), ct);
                var reason = inputs.Select(input => UnwritableReason(input.Argument.BuiltInType, input.Rank, input.Argument.Structure)).FirstOrDefault(r => r != null);
                if (reason != null)
                {
                    unexecutable[reason] = unexecutable.GetValueOrDefault(reason) + 1;
                    Log?.LogVerbose($"{methodId} is executable but has no Exec command: {reason}");
                    continue;
                }
                var outputs = await DescribeArgumentsAsync(session, layouts, methodId, outputArguments, MethodArgumentNaming.PropertyNames([.. outputArguments.Select(a => a.Name)], MethodArgumentNaming.OutputReserved), ct);
                signatures[methodId] = ([.. inputs.Select(i => i.Argument)], [.. outputs.Select(o => o.Argument)]);
            }
            if (unexecutable.Count > 0)
                Log?.LogInfo($"{unexecutable.Values.Sum()} executable methods have no Exec command ({string.Join(", ", unexecutable.Select(kv => $"{kv.Key}: {kv.Value}"))})");
            var kept = candidates.Where(p => signatures.ContainsKey(p.MethodId)).ToList();
            var names = OpcUaNaming.AssignUniqueMethodNames(_settings.BusConfig.ExchangeNamePrefix, kept, message => Log?.LogWarning(message));
            return [.. kept
                .Select(p => new OpcUaMethodItem(p.ObjectId, p.MethodId, p.ObjectId.ToString(), p.MethodId.ToString(), names[p], signatures[p.MethodId].Inputs, signatures[p.MethodId].Outputs))
                .OrderBy(method => method.ExecCommandTypeFullName, StringComparer.Ordinal)];
        }

        /// <summary>The methods whose Executable and UserExecutable attributes are both true.</summary>
        private static async Task<List<NodeId>> ReadExecutableAsync(ISession session, List<NodeId> methodIds, CancellationToken ct)
        {
            List<NodeId> executable = [];
            uint[] attributes = [Attributes.Executable, Attributes.UserExecutable];
            foreach (var chunk in methodIds.Chunk(ReadBatchSize / attributes.Length))
            {
                List<ReadValueId> toRead = [.. chunk.SelectMany(nodeId => attributes.Select(attributeId => new ReadValueId { NodeId = nodeId, AttributeId = attributeId }))];
                var response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither, toRead, ct);
                var results = response.Results;
                for (var i = 0; i < chunk.Length; i++)
                {
                    if (IsTrue(results[i * 2]) && IsTrue(results[i * 2 + 1])) executable.Add(chunk[i]);
                }
            }
            return executable;

            static bool IsTrue(DataValue value) => !StatusCode.IsBad(value.StatusCode) && value.WrappedValue.TryGetValue(out bool flag) && flag;
        }

        /// <summary>
        /// Each method's InputArguments and OutputArguments property values; a method without one of the
        /// properties has no arguments of that kind.
        /// </summary>
        private static async Task<Dictionary<NodeId, (List<Argument> Inputs, List<Argument> Outputs)>> ReadArgumentsAsync(ISession session, List<NodeId> methodIds, CancellationToken ct)
        {
            Dictionary<NodeId, (List<Argument>, List<Argument>)> arguments = methodIds.ToDictionary(m => m, _ => (new List<Argument>(), new List<Argument>()));
            List<(NodeId MethodId, bool IsInput, NodeId Property)> properties = [];
            foreach (var chunk in methodIds.Chunk(BrowseBatchSize))
            {
                List<BrowseDescription> batch = [.. chunk.Select(methodId => new BrowseDescription
                {
                    NodeId = methodId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)NodeClass.Variable,
                    ResultMask = (uint)BrowseResultMask.BrowseName
                })];
                foreach (var (methodId, reference) in await BrowseAllAsync(session, batch, ct))
                {
                    if (reference.NodeId.IsAbsolute || reference.BrowseName.NamespaceIndex != 0) continue;
                    var isInput = reference.BrowseName.Name == BrowseNames.InputArguments;
                    if (!isInput && reference.BrowseName.Name != BrowseNames.OutputArguments) continue;
                    properties.Add((methodId, isInput, ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris)));
                }
            }
            foreach (var chunk in properties.Chunk(ReadBatchSize))
            {
                List<ReadValueId> toRead = [.. chunk.Select(p => new ReadValueId { NodeId = p.Property, AttributeId = Attributes.Value })];
                var response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither, toRead, ct);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var value = response.Results[i];
                    if (StatusCode.IsBad(value.StatusCode) || !value.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> extensions)) continue;
                    var target = chunk[i].IsInput ? arguments[chunk[i].MethodId].Item1 : arguments[chunk[i].MethodId].Item2;
                    foreach (var extension in extensions)
                    {
                        if (extension.TryGetValue(out Argument? argument) && argument != null) target.Add(argument);
                    }
                }
            }
            return arguments;
        }

        private sealed record DescribedArgument(MethodArgument Argument, int Rank);

        private static async Task<List<DescribedArgument>> DescribeArgumentsAsync(ISession session, StructureLayoutBuilder layouts, NodeId methodId, List<Argument> arguments,
            IReadOnlyList<string> propertyNames, CancellationToken ct)
        {
            List<DescribedArgument> described = [];
            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = arguments[i];
                var builtInType = !argument.DataType.IsNull ? await TypeInfo.GetBuiltInTypeAsync(argument.DataType, session.TypeTree, ct) : BuiltInType.Null;
                var structure = await BuildStructureLayoutAsync(layouts, methodId, argument.DataType, builtInType, argument.ValueRank, ct);
                var valueType = OpcUaValueMapper.MapToClrType(builtInType, argument.ValueRank);
                described.Add(new(new MethodArgument(propertyNames[i], builtInType, valueType, structure, argument.ValueRank == ValueRanks.OneDimension), argument.ValueRank));
            }
            return described;
        }

        /// <summary>
        /// Reads access level, data type and value rank for each variable, keeping only those this session may
        /// read and pairing each with the CLR type its value will be published as, or, for a structure the
        /// bridge can decode, with the structure's layout; and deciding which of them get a Set command.
        /// </summary>
        private async Task<Dictionary<NodeId, VariableAttributes>> ReadVariableAttributesAsync(ISession session, List<NodeId> variables, StructureLayoutBuilder layouts, CancellationToken ct)
        {
            Dictionary<NodeId, VariableAttributes> readable = [];
            Dictionary<string, int> unwritable = new(StringComparer.Ordinal);
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
                    var isWritable = (level & AccessLevels.CurrentWrite) != 0 && (_settings.WriteFilter?.IsMatch(chunk[i].ToString()) ?? true);
                    if (isWritable && UnwritableReason(builtInType, rank, structure) is { } reason)
                    {
                        isWritable = false;
                        unwritable[reason] = unwritable.GetValueOrDefault(reason) + 1;
                        Log?.LogVerbose($"{chunk[i]} is writable but has no Set command: {reason}");
                    }
                    readable[chunk[i]] = new(valueType, structure, rank == ValueRanks.OneDimension, builtInType, isWritable);
                }
            }
            if (unwritable.Count > 0)
                Log?.LogInfo($"{unwritable.Values.Sum()} writable items have no Set command ({string.Join(", ", unwritable.Select(kv => $"{kv.Key}: {kv.Value}"))})");
            return readable;
        }

        private sealed record VariableAttributes(Type ValueType, StructureLayout? Structure, bool IsArray, BuiltInType BuiltInType, bool IsWritable);

        /// <summary>Why a variable the session may write still gets no Set command, or null when it gets one.</summary>
        internal static string? UnwritableReason(BuiltInType builtInType, int valueRank, StructureLayout? structure)
        {
            if (valueRank != ValueRanks.Scalar && valueRank != ValueRanks.OneDimension) return "matrix or unfixed rank";
            if (builtInType is BuiltInType.Number or BuiltInType.Integer or BuiltInType.UInteger) return "abstract type";
            if (structure != null) return StructureEncoder.CanEncode(structure) ? null : "no binary encoding";
            return OpcUaValueEncoder.IsSupported(builtInType) ? null : "string fallback";
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
