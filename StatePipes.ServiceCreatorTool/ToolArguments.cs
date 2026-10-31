namespace StatePipes.ServiceCreatorTool
{
    /// <summary>The <c>--name value</c> arguments the tool accepts. Single-dash flags are parsed by Program.</summary>
    internal static class ToolArguments
    {
        public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--repo", "--transport", "--solution-name", "--prefix", "--statemachine-name", "--statemachine", "--state",
            "--parent-state", "--trigger-name", "--trigger-scope", "--event", "--trigger", "--transition-to", "--timer-name",
            "--timer-period", "--moniker", "--dll", "--broker", "--exchange", "--cert", "--certpw", "--timeout",
        };
        public static Dictionary<string, string>? Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) continue;
                var name = args[i];
                if (!Known.Contains(name))
                {
                    Console.WriteLine($"Unknown argument {name}.");
                    return null;
                }
                if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                {
                    Console.WriteLine($"{name} needs a value.");
                    return null;
                }
                values[name] = args[++i];
            }
            return values;
        }
    }
}
