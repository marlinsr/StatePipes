namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Turns a method's argument names into the property names of its Exec command or ExecResult event. An OPC UA
    /// argument name may be empty, repeated, or not an identifier at all, and clients such as StatePipes.Explorer
    /// emit real CLR properties from these names.
    /// </summary>
    internal static class MethodArgumentNaming
    {
        /// <summary>The ExecResult event's own properties, which no output may take.</summary>
        public static readonly IReadOnlySet<string> OutputReserved = new HashSet<string>(StringComparer.Ordinal)
        {
            SelfDescriptionBuilder.ObjectIdProperty, SelfDescriptionBuilder.MethodIdProperty, SelfDescriptionBuilder.StatusCodeProperty, SelfDescriptionBuilder.StatusProperty
        };

        /// <summary>
        /// One property name per argument, in argument order: sanitized like message names, <c>Arg{index}</c> when
        /// empty, prefixed with <c>_</c> when it starts with a digit, and suffixed <c>_2</c>, <c>_3</c>, ... when
        /// <paramref name="reserved"/> or an earlier argument already has it.
        /// </summary>
        public static IReadOnlyList<string> PropertyNames(IReadOnlyList<string?> argumentNames, IReadOnlySet<string> reserved)
        {
            HashSet<string> taken = new(StringComparer.Ordinal);
            List<string> names = [];
            for (var i = 0; i < argumentNames.Count; i++)
            {
                var name = string.IsNullOrEmpty(argumentNames[i]) ? $"Arg{i}" : OpcUaNaming.Sanitize(argumentNames[i]!);
                if (char.IsAsciiDigit(name[0])) name = "_" + name;
                var unique = name;
                for (var suffix = 2; reserved.Contains(unique) || taken.Contains(unique); suffix++) unique = $"{name}_{suffix}";
                taken.Add(unique);
                names.Add(unique);
            }
            return names;
        }
    }
}
