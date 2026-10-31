using System.Text.RegularExpressions;

namespace StatePipes.Test.ClaudePlugin
{
    /// <summary>
    /// Keeps the StatePipes.ClaudePlugin scaffold recipes in step with the ServiceCreatorTool templates.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class RecipeTemplateCheckTest
    {
        private static readonly string[] Operations =
        [
            "new-solution", "new-service", "new-proxy", "update-proxy", "add-state-machine", "add-trigger",
            "add-state", "add-permit-if", "add-permit-reentry-if", "add-ignore-if", "add-periodic", "diagram",
        ];

        private static IReadOnlyList<Recipe> Recipes() => RecipeParser.ParseDirectory(PluginPaths.Operations);

        private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

        private static IEnumerable<string> SampleContents() =>
            Directory.GetFiles(PluginPaths.Resources, "*.sample").Select(f => Normalize(File.ReadAllText(f)));

        /// <summary>Moniker constant name to its marker text, from BaseToolGenerator.cs.</summary>
        private static Dictionary<string, string> ToolMonikers() =>
            Regex.Matches(File.ReadAllText(PluginPaths.BaseToolGenerator), "const string (\\w+Moniker) = \"(.*)\";")
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        /// <summary>
        /// Markers a template places in generated code: written literally, or as a placeholder that
        /// CreateMonikers maps to the marker (e.g. @#$ProxyInjection1@#$ in DefaultSetup_cs.sample).
        /// </summary>
        private static HashSet<string> MarkersInTemplates()
        {
            var monikers = ToolMonikers();
            var samples = SampleContents().ToList();
            var placeholders = Regex.Matches(File.ReadAllText(PluginPaths.BaseToolGenerator), "AddMoniker\\(\"(@#\\$\\w+@#\\$)\", (\\w+Moniker)\\)")
                .Where(m => monikers.ContainsKey(m.Groups[2].Value))
                .Select(m => (Placeholder: m.Groups[1].Value, Marker: monikers[m.Groups[2].Value]));
            var found = monikers.Values.Where(marker => samples.Any(s => s.Contains(marker))).ToHashSet();
            foreach (var (placeholder, marker) in placeholders)
            {
                if (samples.Any(s => s.Contains(placeholder))) found.Add(marker);
            }
            return found;
        }

        [TestMethod]
        public void AllTwelveOperationsHaveARecipe()
        {
            var names = Recipes().Select(r => r.Name).ToHashSet();
            var missing = Operations.Except(names).ToList();
            var extra = names.Except(Operations).ToList();
            Assert.IsTrue(missing.Count == 0 && extra.Count == 0,
                $"Missing recipes: [{string.Join(", ", missing)}]; unexpected recipes: [{string.Join(", ", extra)}]");
        }

        [TestMethod]
        public void EveryTemplateNamedByARecipeExists()
        {
            var problems = Recipes()
                .SelectMany(r => r.Templates.Where(t => !File.Exists(Path.Combine(PluginPaths.Resources, t)))
                    .Select(t => $"{r.Name}.md names template '{t}' that does not exist"))
                .ToList();
            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
        }

        [TestMethod]
        public void EveryMarkerNamedByARecipeIsAToolMoniker()
        {
            var toolMarkers = ToolMonikers().Values.ToHashSet();
            var templateMarkers = MarkersInTemplates();
            var problems = new List<string>();
            foreach (var recipe in Recipes())
            {
                foreach (var marker in recipe.Markers.Select(Normalize))
                {
                    if (!toolMarkers.Contains(marker)) problems.Add($"{recipe.Name}.md cites marker '{marker}' that is not a BaseToolGenerator moniker");
                    else if (!templateMarkers.Contains(marker)) problems.Add($"{recipe.Name}.md cites marker '{marker}' that no template places");
                }
            }
            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
        }

        [TestMethod]
        public void EveryInstalledExternalToolIsCoveredByTheScaffoldSkill()
        {
            var vssettings = Path.Combine(PluginPaths.RepoRoot(), "StatePipes.ServiceCreatorToolSetup", "Resources", "StatePipesExternalToolsSettings.vssettings");
            var titles = Regex.Matches(File.ReadAllText(vssettings), "<Title>([^<]+)</Title>").Select(m => m.Groups[1].Value.Trim()).ToList();
            var skill = File.ReadAllText(Path.Combine(PluginPaths.Operations, "..", "SKILL.md"));
            Assert.IsTrue(titles.Count > 0, "no External Tool titles found in the vssettings file");
            var missing = titles.Where(t => !skill.Contains(t)).ToList();
            Assert.AreEqual(0, missing.Count, $"statepipes-scaffold/SKILL.md does not mention installed External Tools: [{string.Join(", ", missing)}]");
        }

        [TestMethod]
        public void StateMachineGuardRecipesNameTheirGuardTemplate()
        {
            var expected = new Dictionary<string, string[]>
            {
                ["add-permit-if"] = ["PermitIfGuard_cs.sample"],
                ["add-permit-reentry-if"] = ["PermitReentryIfGuard_cs.sample"],
                ["add-ignore-if"] = ["IgnoreIfGuard_cs.sample"],
                ["add-periodic"] = ["PeriodicField_cs.sample", "PeriodicConstructor_cs.sample", "PeriodicOnEntry_cs.sample", "PeriodicOnExit_cs.sample", "PeriodicGuard_cs.sample"],
                ["add-state"] = ["UnparentedState_cs.sample", "FirstParentedState_cs.sample", "ParentedState_cs.sample"],
            };
            var recipes = Recipes().ToDictionary(r => r.Name);
            var problems = new List<string>();
            foreach (var (name, templates) in expected)
            {
                if (!recipes.TryGetValue(name, out var recipe)) { problems.Add($"{name}.md is missing"); continue; }
                problems.AddRange(templates.Except(recipe.Templates).Select(t => $"{name}.md does not name {t}"));
            }
            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
        }
    }
}
