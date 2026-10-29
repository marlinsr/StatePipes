using System.Text.RegularExpressions;

namespace StatePipes.Test.ClaudePlugin
{
    /// <summary>
    /// Pins plugin guidance that local skill runs found missing: Domain-Driven Design folders, a CLAUDE.md for new
    /// solutions, and headless proxy generation whose arguments the ServiceCreatorTool actually parses.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class PluginGuidanceTest
    {
        private static string Skill(string name) => File.ReadAllText(Path.Combine(PluginPaths.Skills, name, "SKILL.md"));
        private static string Operation(string name) => File.ReadAllText(Path.Combine(PluginPaths.Operations, $"{name}.md"));

        [TestMethod]
        public void Conventions_PutEntitiesAndValueObjectsInTheirFolders()
        {
            var conventions = Skill("statepipes-conventions");

            StringAssert.Contains(conventions, "## Domain-Driven Design");
            StringAssert.Contains(conventions, "`<Prefix>/Entities/<Entity>.cs`, namespace `<Prefix>.Entities`");
            StringAssert.Contains(conventions, "`<Prefix>/ValueObjects/<ValueObject>.cs`, namespace `<Prefix>.ValueObjects`");
            StringAssert.Contains(conventions, "Create `<Prefix>/Entities/` when you add the first");
        }

        [TestMethod]
        public void DomainModel_ClassifiesTypesAndMapsThemToFolders()
        {
            var modeling = Skill("statepipes-domain-modeling");

            StringAssert.Contains(modeling, "## Domain model");
            StringAssert.Contains(modeling, "| Each entity | `Entities/<Entity>.cs`");
            StringAssert.Contains(modeling, "| Each value object | `ValueObjects/<ValueObject>.cs` |");
        }

        [TestMethod]
        public void NewSolution_WritesClaudeMdFromThePluginTemplate()
        {
            var template = Path.Combine(PluginPaths.Skills, "statepipes-scaffold", "templates", "CLAUDE.md");

            Assert.IsTrue(File.Exists(template), template);
            var text = File.ReadAllText(template);
            StringAssert.Contains(text, "## Services");
            StringAssert.Contains(text, "<!-- One row per service.");
            StringAssert.Contains(text, "## Domain-Driven Design");
            StringAssert.Contains(Operation("new-solution"), "`templates/CLAUDE.md`");
            StringAssert.Contains(Operation("new-service"), "Services table in `<SolutionDir>/CLAUDE.md`");
        }

        [TestMethod]
        public void ProxyRecipes_RunTheToolHeadlessWithArgumentsItParses()
        {
            var program = File.ReadAllText(Path.Combine(PluginPaths.RepoRoot(), "StatePipes.ServiceCreatorTool", "Program.cs"));
            var parsed = Regex.Matches(program, "args\\[i\\]\\.Equals\\(\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
            var guidance = Operation("new-proxy") + Operation("update-proxy") + Skill("statepipes-scaffold");

            StringAssert.Contains(Operation("new-proxy"), "-headless");
            StringAssert.Contains(Operation("update-proxy"), "-headless");
            var commands = string.Join("\n", Regex.Matches(guidance, "```\\r?\\n(.*?)```", RegexOptions.Singleline).Select(m => m.Groups[1].Value));
            var used = Regex.Matches(commands, "(?<=\\s)(-{1,2}[a-z]+)(?=\\s)").Select(m => m.Groups[1].Value).ToHashSet();
            Assert.IsTrue(used.Contains("--moniker") && used.Contains("--dll") && used.Contains("--broker"), string.Join(", ", used));
            var unknown = used.Where(a => !parsed.Contains(a)).ToList();
            Assert.IsEmpty(unknown, $"Arguments in the proxy guidance that Program.cs doesn't parse: {string.Join(", ", unknown)}");
        }
    }
}
