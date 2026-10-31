using System.Text.RegularExpressions;

namespace StatePipes.Test.ClaudePlugin
{
    internal sealed record Recipe(string Name, string FilePath, IReadOnlyList<string> Templates, IReadOnlyList<string> Markers, IReadOnlyList<string> Creates);

    /// <summary>
    /// Reads the machine lines (Template:, Marker:, Creates:) of a StatePipes.ClaudePlugin scaffold recipe.
    /// </summary>
    internal static class RecipeParser
    {
        private const string TemplatePrefix = "Template:";
        private const string MarkerPrefix = "Marker:";
        private const string CreatesPrefix = "Creates:";

        public static Recipe Parse(string filePath)
        {
            List<string> templates = [];
            List<string> markers = [];
            List<string> creates = [];
            foreach (var rawLine in Regex.Split(File.ReadAllText(filePath), "\r?\n"))
            {
                var line = rawLine.TrimStart();
                if (line.StartsWith(TemplatePrefix, StringComparison.Ordinal)) templates.Add(line[TemplatePrefix.Length..].Trim());
                else if (line.StartsWith(MarkerPrefix, StringComparison.Ordinal)) markers.Add(line[MarkerPrefix.Length..].Trim());
                else if (line.StartsWith(CreatesPrefix, StringComparison.Ordinal)) creates.Add(line[CreatesPrefix.Length..].Trim());
            }
            return new Recipe(Path.GetFileNameWithoutExtension(filePath), filePath, templates, markers, creates);
        }

        public static IReadOnlyList<Recipe> ParseDirectory(string operationsDirectory) =>
            Directory.GetFiles(operationsDirectory, "*.md")
                .Order(StringComparer.Ordinal)
                .Select(Parse)
                .ToList();
    }
}
