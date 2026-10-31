using StatePipes.ServiceCreatorTool;

namespace StatePipes.Test.ClaudePlugin
{
    /// <summary>
    /// Runs the real ServiceCreatorTool generators (no Visual Studio) and checks that the new-solution and
    /// new-service recipes declare exactly the files they create.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ScaffoldGoldenTest
    {
        private string _testRootDirectory = null!;

        [TestInitialize]
        public void Setup()
        {
            _testRootDirectory = Path.Combine(Path.GetTempPath(), $"ScaffoldGoldenTest_{Guid.NewGuid()}");
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

        private static Recipe RecipeNamed(string name) => RecipeParser.Parse(Path.Combine(PluginPaths.Operations, $"{name}.md"));

        private static HashSet<string> Expand(Recipe recipe, string solution, string prefix, string machine) =>
            recipe.Creates
                .Select(c => c.Replace("<Solution>", solution).Replace("<Prefix>", prefix).Replace("<Machine>", machine))
                .ToHashSet();

        private static HashSet<string> FilesUnder(string root) =>
            Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .ToHashSet();

        private static void AssertSameFiles(HashSet<string> expected, HashSet<string> actual)
        {
            var missing = expected.Except(actual).Order().ToList();
            var unexpected = actual.Except(expected).Order().ToList();
            Assert.IsTrue(missing.Count == 0 && unexpected.Count == 0,
                $"Declared by recipe but not generated: [{string.Join(", ", missing)}]{Environment.NewLine}" +
                $"Generated but not declared by recipe: [{string.Join(", ", unexpected)}]");
        }

        /// <summary>Generates a solution the way CreateNewSolutionAndLaunch does and returns its directory.</summary>
        private string GenerateSolution(string enteredName)
        {
            var solutionFileName = enteredName + BaseToolGenerator.solutionExtension;
            var solutionDir = Path.Combine(_testRootDirectory, enteredName);
            new SolutionGeneratorTool(solutionDir, solutionFileName, usingRabbit: true).GenerateSolution();
            return solutionDir;
        }

        [TestMethod]
        public void NewSolution_CreatesExactlyTheFilesTheRecipeDeclares()
        {
            var solutionDir = GenerateSolution("Packages.Acme.Widgets");

            // New StatePipes Sln also adds the first service, named after the product: Acme.Widgets.
            var expected = Expand(RecipeNamed("new-solution"), "Packages.Acme.Widgets", "Acme.Widgets", "WidgetsStateMachine");
            expected.UnionWith(Expand(RecipeNamed("new-service"), "Packages.Acme.Widgets", "Acme.Widgets", "WidgetsStateMachine"));
            AssertSameFiles(expected, FilesUnder(solutionDir));
        }

        [TestMethod]
        public void NewService_CreatesExactlyTheFilesTheRecipeDeclares()
        {
            var solutionDir = GenerateSolution("Packages.Acme.Widgets");
            var before = FilesUnder(solutionDir);

            new ProjectGeneratorTool(solutionDir, "Packages.Acme.Widgets.slnx").GenerateProject("Acme.Widgets.Pump");

            var created = FilesUnder(solutionDir).Except(before).ToHashSet();
            var expected = Expand(RecipeNamed("new-service"), "Packages.Acme.Widgets", "Acme.Widgets.Pump", "PumpStateMachine");
            expected.ExceptWith(before);
            AssertSameFiles(expected, created);
        }

        [TestMethod]
        public void NewSolution_WorksWithPackagesPrefixAndWithout()
        {
            var withPrefix = FilesUnder(GenerateSolution("Packages.Acme.Widgets"));
            var withoutPrefix = FilesUnder(GenerateSolution("Acme.Widgets"));

            static IEnumerable<string> NotSolutionFile(HashSet<string> files) => files.Where(f => !f.EndsWith(".slnx")).Order();
            CollectionAssert.AreEqual(NotSolutionFile(withPrefix).ToList(), NotSolutionFile(withoutPrefix).ToList());
            Assert.AreEqual(1, withPrefix.Count(f => f.EndsWith(".slnx")));
            Assert.AreEqual(1, withoutPrefix.Count(f => f.EndsWith(".slnx")));
        }
    }
}
