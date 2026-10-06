using Opc.Ua;
using System.Security.Cryptography;
using System.Text;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// The namespace and name shared by a node's Get command and Get event. The command is
    /// <c>{Namespace}.{Name}Command</c> and the event <c>{Namespace}.{Name}Event</c>.
    /// </summary>
    internal sealed record MessageNames(string Namespace, string Name)
    {
        public const string CommandSuffix = "Command";
        public const string EventSuffix = "Event";
        public string CommandTypeName => Name + CommandSuffix;
        public string EventTypeName => Name + EventSuffix;
        public string CommandTypeFullName => $"{Namespace}.{CommandTypeName}";
        public string EventTypeFullName => $"{Namespace}.{EventTypeName}";
    }

    /// <summary>
    /// Turns an OPC UA NodeId into the namespace and name of its StatePipes Get command and Get event.
    ///
    /// <para>The namespace mirrors where the node lives: the bridge's exchange name, the node's namespace index,
    /// and, for a string identifier containing dots, everything before its last dot. On exchange <c>Line1Plc</c>,
    /// <c>ns=2;s=Machine.Motor.Speed</c> is in namespace <c>Line1Plc.2.Machine.Motor</c>.</para>
    ///
    /// <para>The name is the namespace index and identifier, in the order and with the letters of the standard
    /// NodeId notation, with dots replaced by underscores: <c>Get_ns2_s_Machine_Motor_Speed</c>. Any other
    /// character that cannot appear in a type name also becomes an underscore, in the name and in each namespace
    /// segment, because consumers such as StatePipes.Explorer emit a real CLR type from it. Non-string
    /// identifiers keep their own letter (<c>i</c>, <c>g</c>, <c>b</c>) so <c>ns=2;i=5</c> and <c>ns=2;s=5</c>
    /// cannot collide.</para>
    /// </summary>
    internal static class OpcUaNaming
    {
        public const string AssemblyName = "StatePipes.OpcUaBridge.Messages";

        /// <summary>A message type's full name is its RabbitMQ routing key, which is capped at 255 bytes.</summary>
        internal const int MaxFullNameLength = 255;
        /// <summary>Bounds the namespace so a long identifier path still leaves room for the type name.</summary>
        internal const int MaxNamespaceLength = 160;
        private const int CollisionSuffixReserve = 4; // "_999"
        private const int HashSuffixLength = 9; // "_" + 8 hex digits

        public static MessageNames ToMessageNames(string exchangeName, NodeId nodeId)
        {
            var identifier = IdentifierText(nodeId);
            var @namespace = Fit(ToNamespace(exchangeName, nodeId, identifier), MaxNamespaceLength, nodeId);
            // Names are pure ASCII, so characters are bytes. Leave room for the separating dot, the longer
            // suffix and a possible collision suffix.
            var maxNameLength = MaxFullNameLength - @namespace.Length - 1 - MessageNames.CommandSuffix.Length - CollisionSuffixReserve;
            var name = Fit($"Get_ns{nodeId.NamespaceIndex}_{IdTypeLetter(nodeId.IdType)}_{Sanitize(identifier)}", maxNameLength, nodeId);
            return new MessageNames(@namespace, name);
        }

        private static string ToNamespace(string exchangeName, NodeId nodeId, string identifier)
        {
            List<string> segments = [.. exchangeName.Split('.'), nodeId.NamespaceIndex.ToString()];
            var lastDot = nodeId.IdType == IdType.String ? identifier.LastIndexOf('.') : -1;
            if (lastDot >= 0) segments.AddRange(identifier[..lastDot].Split('.'));
            return string.Join('.', segments.Select(segment => segment.Length == 0 ? "_" : Sanitize(segment)));
        }

        /// <summary>
        /// Shortens <paramref name="text"/> to <paramref name="maxLength"/>. Truncated texts would collide on a
        /// shared prefix, so the tail is replaced with a hash of the full NodeId.
        /// </summary>
        private static string Fit(string text, int maxLength, NodeId nodeId)
        {
            if (text.Length <= maxLength) return text;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nodeId.ToString())))[..8];
            return $"{text[..(maxLength - HashSuffixLength)]}_{hash}";
        }

        /// <summary>
        /// Assigns every node its names, making the rare collision unique with a numeric suffix on the name.
        /// Collisions are possible because sanitizing is lossy (<c>A-B</c> and <c>A_B</c> both become <c>A_B</c>);
        /// nodes are ordered by their NodeId text first so the same address space always yields the same names.
        /// </summary>
        public static IReadOnlyDictionary<NodeId, MessageNames> AssignUniqueNames(string exchangeName, IEnumerable<NodeId> nodeIds, Action<string>? onCollision = null)
        {
            Dictionary<NodeId, MessageNames> names = [];
            HashSet<string> used = new(StringComparer.Ordinal);
            foreach (var nodeId in nodeIds.Distinct().OrderBy(n => n.ToString(), StringComparer.Ordinal))
            {
                var baseNames = ToMessageNames(exchangeName, nodeId);
                var messageNames = baseNames;
                for (var suffix = 2; !used.Add(messageNames.CommandTypeFullName); suffix++) messageNames = baseNames with { Name = $"{baseNames.Name}_{suffix}" };
                if (messageNames != baseNames) onCollision?.Invoke($"{nodeId} sanitizes to {baseNames.CommandTypeFullName}, which is already taken; exposed as {messageNames.CommandTypeFullName}");
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

        private static string IdentifierText(NodeId nodeId) => nodeId.Identifier switch
        {
            byte[] opaque => Convert.ToBase64String(opaque),
            null => string.Empty,
            var identifier => identifier.ToString() ?? string.Empty
        };

        internal static string Sanitize(string identifier)
        {
            StringBuilder sb = new(identifier.Length);
            foreach (var c in identifier) sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
