using System.Reflection;

namespace StatePipes.ServiceCreatorTool
{
    /// <summary>Writes the service solution's CLAUDE.md from the statepipes plugin's template and keeps its Services table.</summary>
    internal static class ClaudeMdWriter
    {
        public const string FileName = "CLAUDE.md";
        public const string RowMarker = "<!-- One row per service";
        private const string resourceName = "StatePipes.ServiceCreatorTool.Resources.CLAUDE_md.sample";
        public static string Render(string solutionName, string version)
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName) ?? throw new ArgumentException($"Resource '{resourceName}' not found.");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd().Replace("<Solution>", solutionName).Replace("<version>", version);
        }
        public static void WriteNew(string solutionDir, string solutionName, string version)
        {
            var path = Path.Combine(solutionDir, FileName);
            if (File.Exists(path)) return;
            File.WriteAllText(path, Render(solutionName, version));
        }
        /// <summary>Adds the service's row above the marker line; false when there is no CLAUDE.md, no marker, or the row exists.</summary>
        public static bool AddServiceRow(string solutionDir, string prefix)
        {
            var path = Path.Combine(solutionDir, FileName);
            if (!File.Exists(path)) return false;
            var text = File.ReadAllText(path);
            var newLine = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Split(newLine);
            var marker = Array.FindIndex(lines, l => l.StartsWith(RowMarker));
            if (marker < 0 || lines.Any(l => SecondCell(l) == prefix)) return false;
            var component = prefix.Contains('.') ? prefix[(prefix.LastIndexOf('.') + 1)..] : prefix;
            var row = $"| {component} | {prefix} | {prefix}/StatePipesModel.md |";
            File.WriteAllText(path, string.Join(newLine, lines.Take(marker).Append(row).Concat(lines.Skip(marker))));
            return true;
        }
        private static string? SecondCell(string line)
        {
            if (!line.StartsWith('|')) return null;
            var cells = line.Split('|');
            return cells.Length > 3 ? cells[2].Trim() : null;
        }
    }
}
