namespace StatePipes.Test.ClaudePlugin
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class RecipeParserTest
    {
        private string _testRootDirectory = null!;

        [TestInitialize]
        public void Setup()
        {
            _testRootDirectory = Path.Combine(Path.GetTempPath(), $"RecipeParserTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testRootDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testRootDirectory))
            {
                Directory.Delete(_testRootDirectory, recursive: true);
            }
        }

        private string WriteRecipe(string name, string content)
        {
            var path = Path.Combine(_testRootDirectory, $"{name}.md");
            File.WriteAllText(path, content);
            return path;
        }

        [TestMethod]
        public void Parse_ReadsTemplateMarkerAndCreatesLines()
        {
            var path = WriteRecipe("add-permit-if",
                "# Add PermitIf\n" +
                "Template:   A.sample  \n" +
                "Marker: // Injection Point 5, Do Not Delete or Modify\n" +
                "Creates: <Prefix>/X.cs\n");

            var recipe = RecipeParser.Parse(path);

            Assert.AreEqual("add-permit-if", recipe.Name);
            Assert.AreEqual(path, recipe.FilePath);
            CollectionAssert.AreEqual(new[] { "A.sample" }, recipe.Templates.ToArray());
            CollectionAssert.AreEqual(new[] { "// Injection Point 5, Do Not Delete or Modify" }, recipe.Markers.ToArray());
            CollectionAssert.AreEqual(new[] { "<Prefix>/X.cs" }, recipe.Creates.ToArray());
        }

        [TestMethod]
        public void Parse_IgnoresProseAndBlankLines()
        {
            var path = WriteRecipe("diagram",
                "# Diagram\n\nRun the diagrammer. The Template is not named here.\n\n- Marker points are not used\n");

            var recipe = RecipeParser.Parse(path);

            Assert.AreEqual(0, recipe.Templates.Count);
            Assert.AreEqual(0, recipe.Markers.Count);
            Assert.AreEqual(0, recipe.Creates.Count);
        }

        [TestMethod]
        public void Parse_HandlesCrlfLineEndings()
        {
            var path = WriteRecipe("add-state",
                "# Add State\r\nTemplate: UnparentedState_cs.sample\r\nMarker: // Injection Point 5, Do Not Delete or Modify\r\nCreates: <Prefix>/S.cs\r\n");

            var recipe = RecipeParser.Parse(path);

            CollectionAssert.AreEqual(new[] { "UnparentedState_cs.sample" }, recipe.Templates.ToArray());
            CollectionAssert.AreEqual(new[] { "// Injection Point 5, Do Not Delete or Modify" }, recipe.Markers.ToArray());
            CollectionAssert.AreEqual(new[] { "<Prefix>/S.cs" }, recipe.Creates.ToArray());
        }

        [TestMethod]
        public void Parse_ReadsIndentedMachineLines()
        {
            var path = WriteRecipe("add-ignore-if",
                "1. Take the snippet from\n\n   Template: IgnoreIfGuard_cs.sample\n\n   Marker: // Injection Point 5, Do Not Delete or Modify\n");

            var recipe = RecipeParser.Parse(path);

            CollectionAssert.AreEqual(new[] { "IgnoreIfGuard_cs.sample" }, recipe.Templates.ToArray());
            CollectionAssert.AreEqual(new[] { "// Injection Point 5, Do Not Delete or Modify" }, recipe.Markers.ToArray());
        }

        [TestMethod]
        public void ParseDirectory_ReturnsOneRecipePerMarkdownFileSortedByName()
        {
            WriteRecipe("new-service", "Template: B.sample\n");
            WriteRecipe("add-trigger", "Template: A.sample\n");
            File.WriteAllText(Path.Combine(_testRootDirectory, "notes.txt"), "Template: C.sample\n");

            var recipes = RecipeParser.ParseDirectory(_testRootDirectory);

            CollectionAssert.AreEqual(new[] { "add-trigger", "new-service" }, recipes.Select(r => r.Name).ToArray());
        }
    }
}
