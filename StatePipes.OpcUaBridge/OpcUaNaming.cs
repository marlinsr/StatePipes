using Opc.Ua;
using System.Security.Cryptography;
using System.Text;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// The namespace and name base shared by every message of one node: the Get command
    /// <c>{Namespace}.{Name}_Command</c> and Get event <c>{Namespace}.{Name}_Event</c>, and the Monitor, Delete,
    /// MonitorStatus, Set and SetStatus messages, which swap the name's <c>Get_</c> prefix for their own and so carry any collision
    /// suffix too.
    /// </summary>
    internal sealed record MessageNames(string Namespace, string Name)
    {
        public const string GetPrefix = "Get_";
        public const string MonitorPrefix = "Monitor_";
        public const string DeletePrefix = "Delete_";
        public const string MonitorStatusPrefix = "MonitorStatus_";
        public const string SetPrefix = "Set_";
        public const string SetStatusPrefix = "SetStatus_";
        public const string ExecPrefix = "Exec_";
        public const string ExecResultPrefix = "ExecResult_";
        public const string CommandSuffix = "_Command";
        public const string EventSuffix = "_Event";
        /// <summary>How much longer than <c>Get_..._Command</c> the longest name, <c>MonitorStatus_..._Event</c>, is.</summary>
        public const int LongestNameExtra = 8;
        public string CommandTypeName => Name + CommandSuffix;
        public string EventTypeName => Name + EventSuffix;
        public string MonitorCommandTypeName => MonitorPrefix + Base + CommandSuffix;
        public string DeleteCommandTypeName => DeletePrefix + Base + CommandSuffix;
        public string MonitorStatusEventTypeName => MonitorStatusPrefix + Base + EventSuffix;
        public string SetCommandTypeName => SetPrefix + Base + CommandSuffix;
        public string SetStatusEventTypeName => SetStatusPrefix + Base + EventSuffix;
        public string ExecCommandTypeName => ExecPrefix + Base + CommandSuffix;
        public string ExecResultEventTypeName => ExecResultPrefix + Base + EventSuffix;
        public string CommandTypeFullName => $"{Namespace}.{CommandTypeName}";
        public string EventTypeFullName => $"{Namespace}.{EventTypeName}";
        public string MonitorCommandTypeFullName => $"{Namespace}.{MonitorCommandTypeName}";
        public string DeleteCommandTypeFullName => $"{Namespace}.{DeleteCommandTypeName}";
        public string MonitorStatusEventTypeFullName => $"{Namespace}.{MonitorStatusEventTypeName}";
        public string SetCommandTypeFullName => $"{Namespace}.{SetCommandTypeName}";
        public string SetStatusEventTypeFullName => $"{Namespace}.{SetStatusEventTypeName}";
        public string ExecCommandTypeFullName => $"{Namespace}.{ExecCommandTypeName}";
        public string ExecResultEventTypeFullName => $"{Namespace}.{ExecResultEventTypeName}";
        private string Base => Name[GetPrefix.Length..];

        /// <summary>
        /// The MonitorStatus event that answers a Monitor or Delete command, worked out from the command's full name
        /// alone, so a command for a node the bridge does not know can still be answered where its sender listens.
        /// </summary>
        public static bool TryGetMonitorStatusEventFor(string commandFullName, string @namespace, out string statusEventFullName)
        {
            var names = FromCommandFullName(commandFullName, @namespace);
            statusEventFullName = names?.MonitorStatusEventTypeFullName ?? string.Empty;
            return names != null;
        }

        /// <summary>The names of the node a Monitor or Delete command's full name belongs to, or null for any other name.</summary>
        public static MessageNames? FromCommandFullName(string commandFullName, string @namespace)
        {
            var namespacePrefix = @namespace + ".";
            if (!commandFullName.StartsWith(namespacePrefix, StringComparison.Ordinal) || !commandFullName.EndsWith(CommandSuffix, StringComparison.Ordinal)) return null;
            var name = commandFullName[namespacePrefix.Length..^CommandSuffix.Length];
            string? baseName = name.StartsWith(MonitorPrefix, StringComparison.Ordinal) ? name[MonitorPrefix.Length..]
                : name.StartsWith(DeletePrefix, StringComparison.Ordinal) ? name[DeletePrefix.Length..] : null;
            return string.IsNullOrEmpty(baseName) ? null : new MessageNames(@namespace, GetPrefix + baseName);
        }

        /// <summary>
        /// The names of the node a Set command's full name belongs to, or null for any other name. Kept apart from
        /// <see cref="FromCommandFullName"/>, whose answers are MonitorStatus events.
        /// </summary>
        public static MessageNames? FromSetCommandFullName(string commandFullName, string @namespace) => FromPrefixedCommandFullName(commandFullName, @namespace, SetPrefix);

        /// <summary>
        /// The names of the method an Exec command's full name belongs to, or null for any other name. Kept apart
        /// from the other <c>From...</c> methods, whose answers are other events.
        /// </summary>
        public static MessageNames? FromExecCommandFullName(string commandFullName, string @namespace) => FromPrefixedCommandFullName(commandFullName, @namespace, ExecPrefix);

        private static MessageNames? FromPrefixedCommandFullName(string commandFullName, string @namespace, string messagePrefix)
        {
            var prefix = @namespace + "." + messagePrefix;
            if (!commandFullName.StartsWith(prefix, StringComparison.Ordinal) || !commandFullName.EndsWith(CommandSuffix, StringComparison.Ordinal)) return null;
            var baseName = commandFullName[prefix.Length..^CommandSuffix.Length];
            return baseName.Length == 0 ? null : new MessageNames(@namespace, GetPrefix + baseName);
        }
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

        public static MessageNames ToMessageNames(string exchangeName, NodeId nodeId) => ToMessageNames(exchangeName, BaseText(nodeId), nodeId.ToString());

        /// <summary>
        /// The names of a method's Exec messages: the method's NodeId as for an item, or, when
        /// <paramref name="qualifyingObject"/> is given, the object's and the method's joined by a double underscore.
        /// </summary>
        public static MessageNames ToMethodMessageNames(string exchangeName, NodeId? qualifyingObject, NodeId methodId) => qualifyingObject is { } objectId
            ? ToMessageNames(exchangeName, $"{BaseText(objectId)}__{BaseText(methodId)}", $"{objectId}/{methodId}")
            : ToMessageNames(exchangeName, BaseText(methodId), methodId.ToString());

        private static string BaseText(NodeId nodeId) => $"ns{nodeId.NamespaceIndex}_{IdTypeLetter(nodeId.IdType)}_{Sanitize(IdentifierText(nodeId))}";

        private static MessageNames ToMessageNames(string exchangeName, string baseText, string hashSource)
        {
            var @namespace = MessageNamespace(exchangeName);
            // Names are pure ASCII, so characters are bytes. Leave room for the separating dot, the longer
            // suffix, a possible collision suffix and the longest per-item message name (MonitorStatus_..._Event).
            var maxNameLength = MaxFullNameLength - @namespace.Length - 1 - MessageNames.CommandSuffix.Length - CollisionSuffixReserve
                - MessageNames.LongestNameExtra;
            return new MessageNames(@namespace, Fit(MessageNames.GetPrefix + baseText, maxNameLength, hashSource));
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
        public static IReadOnlyDictionary<NodeId, MessageNames> AssignUniqueNames(string exchangeName, IEnumerable<NodeId> nodeIds, Action<string>? onCollision = null) =>
            AssignUnique(nodeIds.Distinct().Select(nodeId => (nodeId, nodeId.ToString(), ToMessageNames(exchangeName, nodeId))),
                names => names.CommandTypeFullName, onCollision);

        /// <summary>
        /// Assigns every (object, method) pair the names of its Exec messages, as <see cref="AssignUniqueNames"/>
        /// does for nodes. A method node reached from more than one object is named with the object in every pair,
        /// so no object holds the bare name by the order the server lists them in.
        /// </summary>
        public static IReadOnlyDictionary<(NodeId ObjectId, NodeId MethodId), MessageNames> AssignUniqueMethodNames(string exchangeName,
            IEnumerable<(NodeId ObjectId, NodeId MethodId)> pairs, Action<string>? onCollision = null)
        {
            var distinct = pairs.Distinct().ToList();
            var shared = distinct.GroupBy(p => p.MethodId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            return AssignUnique(distinct.Select(p => (p, $"{p.MethodId} on {p.ObjectId}", ToMethodMessageNames(exchangeName, shared.Contains(p.MethodId) ? p.ObjectId : null, p.MethodId))),
                names => names.ExecCommandTypeFullName, onCollision);
        }

        /// <summary>
        /// Makes each key's names unique with a numeric suffix. Keys are ordered by their text so the same address
        /// space always yields the same names, and a suffixed name never takes a name that another key has on its own merit.
        /// </summary>
        private static Dictionary<TKey, MessageNames> AssignUnique<TKey>(IEnumerable<(TKey Key, string Text, MessageNames Names)> keys,
            Func<MessageNames, string> exposedAs, Action<string>? onCollision) where TKey : notnull
        {
            var baseNames = keys.OrderBy(k => k.Text, StringComparer.Ordinal).ToList();
            HashSet<string> reserved = new(baseNames.Select(b => b.Names.Name), StringComparer.Ordinal);
            Dictionary<string, string> holders = new(StringComparer.Ordinal);
            Dictionary<TKey, MessageNames> names = [];
            foreach (var (key, text, baseName) in baseNames)
            {
                var messageNames = baseName;
                if (holders.TryGetValue(baseName.Name, out var holder))
                {
                    var suffix = 2;
                    while (!reserved.Add($"{baseName.Name}_{suffix}")) suffix++;
                    messageNames = baseName with { Name = $"{baseName.Name}_{suffix}" };
                    onCollision?.Invoke($"{text} and {holder} both map to {baseName.Name}; {text} is exposed as {exposedAs(messageNames)}");
                }
                holders.Add(messageNames.Name, text);
                names.Add(key, messageNames);
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
