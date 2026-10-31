using StatePipes.Test.ClaudePlugin;
using System.Text.RegularExpressions;

namespace StatePipes.Test.ServiceCreatorTool
{
    /// <summary>The Diagrammer uses top-level statements, so its arguments are checked in source; HeadlessEndToEndTest runs it.</summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class DiagrammerArgumentsTest
    {
        [TestMethod]
        public void Diagrammer_ParsesHeadlessAndOut()
        {
            var program = File.ReadAllText(Path.Combine(PluginPaths.RepoRoot(), "StatePipes.Diagrammer", "Program.cs"));
            var parsed = Regex.Matches(program, "Equals\\(\"(-headless|--out)\"").Select(m => m.Groups[1].Value).ToHashSet();

            CollectionAssert.AreEquivalent(new[] { "-headless", "--out" }, parsed.ToArray());
            StringAssert.Contains(program, "Finished!!!");
        }
    }
}
