using System.Text.Json;

namespace StatePipes.ServiceCreatorToolSetup
{
    /// <summary>
    /// Registers the installed statepipes plugin folder as the statepipes Claude Code marketplace and installs the plugin.
    /// A plugin from a folder loads in place, so the Installer replacing the folder is its update.
    /// </summary>
    internal sealed class ClaudePluginSetup(ICommandRunner runner, string? claudePath, string marketplaceDir, TextWriter output)
    {
        private const string marketplaceName = "statepipes";
        private const string pluginId = "statepipes@statepipes";
        private sealed class CommandFailedException(string message) : Exception(message) { }
        public void Register()
        {
            if (claudePath == null)
            {
                output.WriteLine("Claude Code not found; to add the StatePipes plugin later, run:");
                WriteManualCommands();
                return;
            }
            try
            {
                var marketplace = FindMarketplace();
                var isLocal = marketplace != null && IsInstallFolder(marketplace.Value);
                if (marketplace != null && !isLocal)
                {
                    if (PluginInstalled()) Claude("plugin", "uninstall", pluginId);
                    Claude("plugin", "marketplace", "remove", marketplaceName);
                }
                if (!isLocal) Claude("plugin", "marketplace", "add", marketplaceDir);
                Claude("plugin", "install", pluginId);
                var version = PluginVersion();
                output.WriteLine(version == null ? "StatePipes Claude Code plugin installed." : $"StatePipes Claude Code plugin {version} installed.");
            }
            catch (CommandFailedException e)
            {
                output.WriteLine(e.Message);
                WriteManualCommands();
            }
        }
        public void Remove()
        {
            if (claudePath == null)
            {
                output.WriteLine("Claude Code not found; StatePipes plugin not removed.");
                return;
            }
            try
            {
                var marketplace = FindMarketplace();
                if (marketplace == null || !IsInstallFolder(marketplace.Value)) return;
                if (PluginInstalled()) Claude("plugin", "uninstall", pluginId);
                Claude("plugin", "marketplace", "remove", marketplaceName);
                output.WriteLine("StatePipes plugin removed from Claude Code.");
            }
            catch (CommandFailedException e)
            {
                output.WriteLine(e.Message);
            }
        }
        public static string? FindClaude()
        {
            var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var folder in folders)
            {
                foreach (var name in new[] { "claude.exe", "claude.cmd" })
                {
                    var candidate = Path.Combine(folder.Trim('"'), name);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
            return File.Exists(local) ? local : null;
        }
        public static string InstalledMarketplaceDir() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ClaudePlugin"));
        private void WriteManualCommands()
        {
            output.WriteLine($"  claude plugin marketplace add \"{marketplaceDir}\"");
            output.WriteLine($"  claude plugin install {pluginId}");
        }
        private string Claude(params string[] arguments)
        {
            var result = runner.Run(claudePath!, arguments);
            if (result.ExitCode != 0) throw Failed(arguments, $"{result.Output} {result.Error}");
            return result.Output;
        }
        private static CommandFailedException Failed(string[] arguments, string reason) =>
            new($"Claude Code plugin setup failed: claude {string.Join(" ", arguments)}: {reason.Trim()}");
        private JsonElement ParseArray(params string[] arguments)
        {
            var text = Claude(arguments);
            try
            {
                var root = JsonDocument.Parse(text).RootElement;
                if (root.ValueKind != JsonValueKind.Array) throw Failed(arguments, $"unexpected output: {text}");
                return root;
            }
            catch (JsonException)
            {
                throw Failed(arguments, $"unexpected output: {text}");
            }
        }
        private JsonElement? FindMarketplace()
        {
            foreach (var entry in ParseArray("plugin", "marketplace", "list", "--json").EnumerateArray())
                if (StringProperty(entry, "name") == marketplaceName) return entry;
            return null;
        }
        private bool PluginInstalled() =>
            ParseArray("plugin", "list", "--json").EnumerateArray().Any(entry => StringProperty(entry, "id") == pluginId);
        private bool IsInstallFolder(JsonElement marketplace) =>
            StringProperty(marketplace, "source") == "directory" && StringProperty(marketplace, "path") is string path && SamePath(path, marketplaceDir);
        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        private static string? StringProperty(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private string? PluginVersion()
        {
            try
            {
                var json = File.ReadAllText(Path.Combine(marketplaceDir, "statepipes", ".claude-plugin", "plugin.json"));
                return StringProperty(JsonDocument.Parse(json).RootElement, "version");
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
