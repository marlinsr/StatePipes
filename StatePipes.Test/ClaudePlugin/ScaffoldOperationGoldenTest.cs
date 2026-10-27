using StatePipes.ServiceCreatorTool;

namespace StatePipes.Test.ClaudePlugin
{
    /// <summary>
    /// Runs the add-* generators (no Visual Studio, no dialogs) on a generated service and checks each recipe's
    /// Creates: files, that every Marker: it cites gains inserted text, and the using-prefix handling it describes.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ScaffoldOperationGoldenTest
    {
        private const string SolutionName = "Packages.Acme.Widgets";
        private const string Prefix = "Acme.Widgets";
        private const string Machine = "WidgetsStateMachine";
        private const string PublicPrefix = "//@#$PublicTriggers@#$";
        private const string InternalPrefix = "//@#$InternalTriggers@#$";

        private string _testRootDirectory = null!;
        private string _solutionDir = null!;
        private string _projectDir = null!;
        private string _targetDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _testRootDirectory = Path.Combine(Path.GetTempPath(), $"ScaffoldOperationGoldenTest_{Guid.NewGuid()}");
            _solutionDir = Path.Combine(_testRootDirectory, SolutionName);
            new SolutionGeneratorTool(_solutionDir, SolutionName + BaseToolGenerator.solutionExtension, usingRabbit: true).GenerateSolution();
            _projectDir = Path.Combine(_solutionDir, Prefix);
            _targetDir = Path.Combine(_projectDir, "bin", "Debug", "net10.0");
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

        private static HashSet<string> Expand(Recipe recipe, params (string Placeholder, string Value)[] values) =>
            recipe.Creates.Select(c => values.Aggregate(c, (s, v) => s.Replace(v.Placeholder, v.Value))).ToHashSet();

        private HashSet<string> Files() =>
            Directory.GetFiles(_solutionDir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_solutionDir, f).Replace('\\', '/'))
                .ToHashSet();

        private HashSet<string> CreatedBy(Action generate)
        {
            var before = Files();
            generate();
            return Files().Except(before).ToHashSet();
        }

        private static void AssertSameFiles(string recipe, IEnumerable<string> expected, IEnumerable<string> actual)
        {
            var missing = expected.Except(actual).Order().ToList();
            var unexpected = actual.Except(expected).Order().ToList();
            Assert.IsTrue(missing.Count == 0 && unexpected.Count == 0,
                $"{recipe}.md: declared but not generated [{string.Join(", ", missing)}]; generated but not declared [{string.Join(", ", unexpected)}]");
        }

        /// <summary>Asserts every marker the recipe cites is still in the file once, with new text inserted above it.</summary>
        private static void AssertMarkersGainedText(Recipe recipe, string beforeText, string afterText)
        {
            var before = beforeText.Replace("\r\n", "\n").Split('\n');
            var after = afterText.Replace("\r\n", "\n").Split('\n');
            foreach (var marker in recipe.Markers)
            {
                var afterIndex = Array.FindIndex(after, l => l.Trim() == marker);
                var beforeIndex = Array.FindIndex(before, l => l.Trim() == marker);
                Assert.IsTrue(beforeIndex > 0 && afterIndex > 0, $"{recipe.Name}.md: marker '{marker}' not found in the state file");
                Assert.AreEqual(1, after.Count(l => l.Trim() == marker), $"{recipe.Name}.md: marker '{marker}' must remain exactly once");
                Assert.AreNotEqual(before[beforeIndex - 1], after[afterIndex - 1], $"{recipe.Name}.md: nothing was inserted above '{marker}'");
            }
        }

        private string AddState(string state) =>
            new StateGeneratorTool(_solutionDir, SolutionName).GenerateState(_projectDir, Prefix, _targetDir, Machine, state, parentStateName: null, isFirstChild: false);

        [TestMethod]
        public void AddStateMachine_CreatesTheRecipeFiles()
        {
            var created = CreatedBy(() => new StateMachineGeneratorTool(_solutionDir, SolutionName)
                .GenerateStateMachine(_projectDir, Prefix, _targetDir, "Valve", isStateMachine: true));

            AssertSameFiles("add-state-machine", Expand(RecipeNamed("add-state-machine"), ("<Prefix>", Prefix), ("<Machine>", "Valve")), created);
        }

        [TestMethod]
        public void AddTrigger_PublicAndInternal_CreateTheRecipeFiles()
        {
            var recipe = RecipeNamed("add-trigger");
            // A generator instance holds its paths, so each call needs its own instance (as each tool run does).
            var publicFile = CreatedBy(() => new TriggerGeneratorTool(_solutionDir, SolutionName).GenerateTrigger(_projectDir, Prefix, _targetDir, Machine, "Start", isInternal: false));
            var internalFile = CreatedBy(() => new TriggerGeneratorTool(_solutionDir, SolutionName).GenerateTrigger(_projectDir, Prefix, _targetDir, Machine, "Tick", isInternal: true));

            var declared = Expand(recipe, ("<Prefix>", Prefix), ("<Machine>", Machine), ("<Trigger>", "Start"))
                .Union(Expand(recipe, ("<Prefix>", Prefix), ("<Machine>", Machine), ("<Trigger>", "Tick")));
            AssertSameFiles("add-trigger",
                [$"{Prefix}/StateMachines/{Machine}/Triggers/Start.cs", $"{Prefix}/StateMachines/{Machine}/Triggers/Internal/Tick.cs"],
                publicFile.Union(internalFile));
            Assert.IsTrue(publicFile.Union(internalFile).All(declared.Contains), "add-trigger.md: a generated trigger path is not declared");
        }

        [TestMethod]
        public void AddState_CreatesTheRecipeFileWithEveryMarker()
        {
            string statePath = null!;
            var created = CreatedBy(() => statePath = AddState("Idle"));

            AssertSameFiles("add-state", Expand(RecipeNamed("add-state"), ("<Prefix>", Prefix), ("<Machine>", Machine), ("<State>", "Idle")), created);
            var text = File.ReadAllText(statePath);
            foreach (var marker in RecipeNamed("add-state").Markers) Assert.IsTrue(text.Contains(marker), $"add-state.md: '{marker}' missing from new state");
            Assert.IsTrue(text.Contains(PublicPrefix) && text.Contains(InternalPrefix), "add-state.md: new state must keep both using prefixes");
        }

        [TestMethod]
        public void AddPermitIf_InsertsAboveTheMarkerAndEnablesThePublicUsing()
        {
            var statePath = AddState("Idle");
            var before = File.ReadAllText(statePath);

            new PermitIfGaurdGeneratorTool(_solutionDir, SolutionName).GeneratePermitIfGuard(Prefix, statePath, "Start", triggerIsInternal: false, "Running");

            var after = File.ReadAllText(statePath);
            AssertMarkersGainedText(RecipeNamed("add-permit-if"), before, after);
            Assert.IsFalse(after.Contains(PublicPrefix), "add-permit-if.md: public using prefix must be removed for a public trigger");
            Assert.IsTrue(after.Contains(InternalPrefix), "add-permit-if.md: internal using prefix must stay for a public trigger");
        }

        [TestMethod]
        public void AddPermitReentryIf_InsertsAboveTheMarkerAndEnablesTheInternalUsing()
        {
            var statePath = AddState("Running");
            var before = File.ReadAllText(statePath);

            new PermitReentryIfGaurdGeneratorTool(_solutionDir, SolutionName).GeneratePermitReentryIfGuard(Prefix, statePath, "Tick", triggerIsInternal: true);

            var after = File.ReadAllText(statePath);
            AssertMarkersGainedText(RecipeNamed("add-permit-reentry-if"), before, after);
            Assert.IsFalse(after.Contains(InternalPrefix), "add-permit-reentry-if.md: internal using prefix must be removed for an internal trigger");
            Assert.IsTrue(after.Contains(PublicPrefix), "add-permit-reentry-if.md: public using prefix must stay for an internal trigger");
        }

        [TestMethod]
        public void AddIgnoreIf_InsertsAboveTheMarkerAndEnablesThePublicUsing()
        {
            var statePath = AddState("Running");
            var before = File.ReadAllText(statePath);

            new IgnoreIfGaurdGeneratorTool(_solutionDir, SolutionName).GenerateIgnoreIfGuard(Prefix, statePath, "Start", triggerIsInternal: false);

            var after = File.ReadAllText(statePath);
            AssertMarkersGainedText(RecipeNamed("add-ignore-if"), before, after);
            Assert.IsFalse(after.Contains(PublicPrefix), "add-ignore-if.md: public using prefix must be removed for a public trigger");
        }

        [TestMethod]
        public void AddPeriodic_CreatesTheTriggerAndInsertsAboveEveryMarker()
        {
            var statePath = AddState("Running");
            var before = File.ReadAllText(statePath);
            var recipe = RecipeNamed("add-periodic");

            var created = CreatedBy(() => new PeriodicTriggerGeneratorTool(_solutionDir, SolutionName)
                .GeneratePeriodicTrigger(_projectDir, Prefix, _targetDir, statePath, Machine, "poll", "500"));

            AssertSameFiles("add-periodic", Expand(recipe, ("<Prefix>", Prefix), ("<Machine>", Machine), ("<Name>", "Poll")), created);
            var after = File.ReadAllText(statePath);
            AssertMarkersGainedText(recipe, before, after);
            Assert.IsTrue(after.Contains("PollPeriodicWorker"), "add-periodic.md: guard method must be <Name>PeriodicWorker");
            Assert.IsFalse(after.Contains(InternalPrefix), "add-periodic.md: internal using prefix must be removed");
        }
    }
}
