using Opc.Ua;
using System.Security.Cryptography;
using System.Text;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// The namespace and name shared by a node's Get command and Get event. The command is
    /// <c>{Namespace}.{Name}_Command</c> and the event <c>{Namespace}.{Name}_Event</c>. The Monitor command
    /// swaps the name's <c>Get_</c> prefix for <c>Monitor_</c>, so it carries any collision suffix too.
    /// </summary>
    internal sealed record MessageNames(string Namespace, string Name)
    {
        public const string GetPrefix = "Get_";
        public const string MonitorPrefix = "Monitor_";
        public const string CommandSuffix = "_Command";
        public const string EventSuffix = "_Event";
        public string CommandTypeName => Name + CommandSuffix;
        public string EventTypeName => Name + EventSuffix;
        public string MonitorCommandTypeName => MonitorPrefix + Name[GetPrefix.Length..] + CommandSuffix;
        public string CommandTypeFullName => $"{Namespace}.{CommandTypeName}";
        public string EventTypeFullName => $"{Namespace}.{EventTypeName}";
        public string MonitorCommandTypeFullName => $"{Namespace}.{MonitorCommandTypeName}";
    }

    /// <summary>
    /// Turns an OPC UA NodeId into the namespace and name of its StatePipes Get command and Get event.
    ///
    /// <para>The namespace is the bridge's exchange name, the same for every node.</para>
    ///
    /// <para>The name is the whole NodeId, with the namespace index and the letter of its identifier type as in
    /// the standard NodeId notation (<c>i</c>, <c>s</c>, <c>g</c>, <c>b</c>), and dots replaced by underscores:
    /// on exchange <c>Line1Plc</c>, <c>ns=2;s=Machine.Motor.Speed</c> becomes
    /// <c>Line1Plc.Get_ns2_s_Machine_Motor_Speed</c>. Any other character that cannot appear in a type name also
    /// becomes an underscore, in the name and in each exchange name segment, because consumers such as
    /// StatePipes.Explorer emit a real CLR type from it.</para>
    /// </summary>
    internal static class OpcUaNaming
    {
        public const string AssemblyName = "StatePipes.OpcUaBridge.Messages";

        /// <summary>A message type's full name is its RabbitMQ routing key, which is capped at 255 bytes.</summary>
        internal const int MaxFullNameLength = 255;
        /// <summary>Bounds the namespace so a long exchange name still leaves room for the type name.</summary>
        internal const int MaxNamespaceLength = 160;
        private const int CollisionSuffixReserve = 4; // "_999"
        private const int HashSuffixLength = 9; // "_" + 8 hex digits

        /// <summary>The namespace every message name on <paramref name="exchangeName"/> shares.</summary>
        public static string MessageNamespace(string exchangeName)
        {
            var unfittedNamespace = ToNamespace(exchangeName);
            return Fit(unfittedNamespace, MaxNamespaceLength, unfittedNamespace);
        }

        public static MessageNames ToMessageNames(string exchangeName, NodeId nodeId)
        {
            var identifier = IdentifierText(nodeId);
            var @namespace = MessageNamespace(exchangeName);
            // Names are pure ASCII, so characters are bytes. Leave room for the separating dot, the longer
            // suffix, a possible collision suffix and the Monitor command's longer prefix.
            var maxNameLength = MaxFullNameLength - @namespace.Length - 1 - MessageNames.CommandSuffix.Length - CollisionSuffixReserve
                - (MessageNames.MonitorPrefix.Length - MessageNames.GetPrefix.Length);
            var name = Fit($"{MessageNames.GetPrefix}ns{nodeId.NamespaceIndex}_{IdTypeLetter(nodeId.IdType)}_{Sanitize(identifier)}", maxNameLength, nodeId.ToString());
            return new MessageNames(@namespace, name);
        }

        /// <summary>
        /// Names the type a structure DataType is published as: <c>{exchange}.Types.ns{index}_{letter}_{identifier}</c>,
        /// so <c>ns=2;s=DensityDataType</c> on <c>Line1Plc</c> becomes <c>Line1Plc.Types.ns2_s_DensityDataType</c>.
        /// Built from the DataType's NodeId like message names, so two DataTypes never share a name.
        /// </summary>
        public static (string Namespace, string Name) ToStructureTypeNames(string exchangeName, NodeId dataTypeId)
        {
            var @namespace = MessageNamespace(exchangeName) + ".Types";
            var maxNameLength = MaxFullNameLength - @namespace.Length - 1;
            var name = Fit($"ns{dataTypeId.NamespaceIndex}_{IdTypeLetter(dataTypeId.IdType)}_{Sanitize(IdentifierText(dataTypeId))}", maxNameLength, dataTypeId.ToString());
            return (@namespace, name);
        }

        private static string ToNamespace(string exchangeName) =>
            string.Join('.', exchangeName.Split('.').Select(segment => segment.Length == 0 ? "_" : Sanitize(segment)));

        /// <summary>
        /// Shortens <paramref name="text"/> to <paramref name="maxLength"/>. Truncated texts would collide on a
        /// shared prefix, so the tail is replaced with a hash of <paramref name="hashSource"/>.
        /// </summary>
        private static string Fit(string text, int maxLength, string hashSource)
        {
            if (text.Length <= maxLength) return text;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashSource)))[..8];
            return $"{text[..(maxLength - HashSuffixLength)]}_{hash}";
        }

        /// <summary>
        /// Assigns every node its names, making the rare collision unique with a numeric suffix on the name.
        /// Collisions are possible because sanitizing is lossy (<c>A-B</c> and <c>A_B</c> both become <c>A_B</c>).
        /// Nodes are ordered by their NodeId text so the same address space always yields the same
        /// names, and a suffixed name never takes a name that another node has on its own merit.
        /// </summary>
        public static IReadOnlyDictionary<NodeId, MessageNames> AssignUniqueNames(string exchangeName, IEnumerable<NodeId> nodeIds, Action<string>? onCollision = null)
        {
            var baseNames = nodeIds.Distinct()
                .OrderBy(n => n.ToString(), StringComparer.Ordinal)
                .Select(nodeId => (NodeId: nodeId, Names: ToMessageNames(exchangeName, nodeId)))
                .ToList();
            HashSet<string> reserved = new(baseNames.Select(b => b.Names.Name), StringComparer.Ordinal);
            Dictionary<string, NodeId> holders = new(StringComparer.Ordinal);
            Dictionary<NodeId, MessageNames> names = [];
            foreach (var (nodeId, baseName) in baseNames)
            {
                var messageNames = baseName;
                if (holders.TryGetValue(baseName.Name, out var holder))
                {
                    var suffix = 2;
                    while (!reserved.Add($"{baseName.Name}_{suffix}")) suffix++;
                    messageNames = baseName with { Name = $"{baseName.Name}_{suffix}" };
                    onCollision?.Invoke($"{nodeId} and {holder} both map to {baseName.Name}; {nodeId} is exposed as {messageNames.CommandTypeFullName}");
                }
                holders.Add(messageNames.Name, nodeId);
                names.Add(nodeId, messageNames);
            }
            return names;
        }

        private static string IdTypeLetter(IdType idType) => idType switch
        {
            IdType.Numeric => "i",
            IdType.String => "s",
            IdType.Guid => "g",
            IdType.Opaque => "b",
            _ => "x"
        };

        private static string IdentifierText(NodeId nodeId) => nodeId.TryGetValue(out ByteString opaque)
            ? Convert.ToBase64String(opaque.Span)
            : nodeId.IdentifierAsString ?? string.Empty;

        internal static string Sanitize(string identifier)
        {
            StringBuilder sb = new(identifier.Length);
            foreach (var c in identifier) sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
