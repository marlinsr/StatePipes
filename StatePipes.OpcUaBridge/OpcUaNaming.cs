using Opc.Ua;
using System.Security.Cryptography;
using System.Text;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Turns an OPC UA NodeId into the name of its StatePipes Get command and Get event.
    ///
    /// <para>The name is the namespace index and identifier, in the order and with the letters of the standard
    /// NodeId notation: <c>ns=2;s=Line1.Temperature</c> becomes <c>Get_ns2_s_Line1_Temperature</c>. Dots become
    /// underscores, as does any other character that cannot appear in a type name, because consumers such as
    /// StatePipes.Explorer emit a real CLR type from the name. Non-string identifiers keep their own letter
    /// (<c>i</c>, <c>g</c>, <c>b</c>) so <c>ns=2;i=5</c> and <c>ns=2;s=5</c> cannot collide.</para>
    ///
    /// <para>The command and event share the name and are told apart by namespace, so the Get command for a
    /// node is <c>StatePipes.OpcUaBridge.Commands.Get_...</c> and its Get event is
    /// <c>StatePipes.OpcUaBridge.Events.Get_...</c>.</para>
    /// </summary>
    internal static class OpcUaNaming
    {
        public const string CommandNamespace = "StatePipes.OpcUaBridge.Commands";
        public const string EventNamespace = "StatePipes.OpcUaBridge.Events";
        public const string AssemblyName = "StatePipes.OpcUaBridge.Messages";

        /// <summary>
        /// The message type's full name is its RabbitMQ routing key, which is capped at 255 bytes. Names are pure
        /// ASCII, so this leaves room for the longer of the two namespaces plus a collision suffix.
        /// </summary>
        internal const int MaxMessageNameLength = 255 - 32 - 4; // "StatePipes.OpcUaBridge.Commands." is 32, "_999" is 4
        private const int HashSuffixLength = 9; // "_" + 8 hex digits

        public static string ToMessageName(NodeId nodeId)
        {
            var name = $"Get_ns{nodeId.NamespaceIndex}_{IdTypeLetter(nodeId.IdType)}_{Sanitize(IdentifierText(nodeId))}";
            if (name.Length <= MaxMessageNameLength) return name;
            // Truncated names would collide on a shared prefix, so the tail is replaced with a hash of the full id.
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nodeId.ToString())))[..8];
            return $"{name[..(MaxMessageNameLength - HashSuffixLength)]}_{hash}";
        }

        /// <summary>
        /// Assigns every node a message name, making the rare collision unique with a numeric suffix. Collisions
        /// are possible because sanitizing is lossy (<c>A.B</c> and <c>A_B</c> both become <c>A_B</c>); nodes are
        /// ordered by their NodeId text first so the same address space always yields the same names.
        /// </summary>
        public static IReadOnlyDictionary<NodeId, string> AssignUniqueNames(IEnumerable<NodeId> nodeIds, Action<string>? onCollision = null)
        {
            Dictionary<NodeId, string> names = [];
            HashSet<string> used = new(StringComparer.Ordinal);
            foreach (var nodeId in nodeIds.Distinct().OrderBy(n => n.ToString(), StringComparer.Ordinal))
            {
                var baseName = ToMessageName(nodeId);
                var name = baseName;
                for (var suffix = 2; !used.Add(name); suffix++) name = $"{baseName}_{suffix}";
                if (name != baseName) onCollision?.Invoke($"{nodeId} sanitizes to {baseName}, which is already taken; exposed as {name}");
                names.Add(nodeId, name);
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
