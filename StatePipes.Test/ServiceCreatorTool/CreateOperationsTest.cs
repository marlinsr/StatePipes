using StatePipes.ServiceCreatorTool;
using static StatePipes.Test.ServiceCreatorTool.ToolTestHelpers;

namespace StatePipes.Test.ServiceCreatorTool
{
    /// <summary>
    /// Runs the Create operations headless on a generated solution. Only operations that don't read the built service DLL are
    /// covered here; the rest run in HeadlessEndToEndTest.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class CreateOperationsTest
    {
        private const string SolutionName = "Packages.Acme.Widgets";
        private const string Prefix = "Acme.Widgets";
        private string _root = null!;
        private string _solutionDir = null!;
        private string _projectDir = null!;
        private string _targetDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), $"CreateOperationsTest_{Guid.NewGuid()}");
            _solutionDir = Path.Combine(_root, SolutionName);
            new SolutionGeneratorTool(_solutionDir, SolutionName + BaseToolGenerator.solutionExtension, usingRabbit: true).GenerateSolution();
            _projectDir = Path.Combine(_solutionDir, Prefix);
            _targetDir = Path.Combine(_projectDir, "obj", "Debug", "net10.0");
        }

        [TestCleanup]
        public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

        private delegate ToolResult ProjectOperation(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, IToolInput input);

        private ToolResult Run(ProjectOperation operation, params (string Arg, string Value)[] args) =>
            operation(_solutionDir, SolutionName + BaseToolGenerator.solutionExtension, Prefix + ".csproj", _targetDir, Headless(args));

        private Dictionary<string, DateTime> Snapshot() =>
            Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.GetLastWriteTimeUtc);

        private void AssertUnchanged(Dictionary<string, DateTime> before) =>
            CollectionAssert.AreEquivalent(before.ToList(), Snapshot().ToList(), "files changed");

        [TestMethod]
        public void StateMachine_Headless_CreatesFiles()
        {
            Assert.IsTrue(Run(StateMachineGeneratorTool.CreateNewStateMachine, ("--statemachine-name", "Valve")).Succeeded);

            Assert.IsTrue(File.Exists(Path.Combine(_projectDir, "StateMachines", "Valve", "Valve.cs")));
        }

        [TestMethod]
        public void StateMachine_Existing_RefusesAndWritesNothing()
        {
            Run(StateMachineGeneratorTool.CreateNewStateMachine, ("--statemachine-name", "Valve"));
            var before = Snapshot();

            var output = Capture(() => Assert.IsFalse(Run(StateMachineGeneratorTool.CreateNewStateMachine, ("--statemachine-name", "Valve")).Succeeded));

            StringAssert.Contains(output, Path.Combine(Prefix, "StateMachines", "Valve", "Valve.cs") + " already exists.");
            AssertUnchanged(before);
        }

        [TestMethod]
        public void StateMachine_Missing_FailsWithNeeds()
        {
            var before = Snapshot();

            var output = Capture(() => Assert.IsFalse(Run(StateMachineGeneratorTool.CreateNewStateMachine).Succeeded));

            StringAssert.Contains(output, "Headless mode needs --statemachine-name.");
            AssertUnchanged(before);
        }

        [TestMethod]
        public void Event_Headless_CreatesFile()
        {
            var result = Run(EventGeneratorTool.CreateNewEvent, ("--event", "StatusEvent"));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(Path.Combine(_projectDir, "Events", "StatusEvent.cs"), result.FileToOpen);
            Assert.IsTrue(File.Exists(result.FileToOpen));
        }

        [TestMethod]
        public void Event_Missing_FailsWithNeeds()
        {
            var before = Snapshot();

            var output = Capture(() => Assert.IsFalse(Run(EventGeneratorTool.CreateNewEvent).Succeeded));

            StringAssert.Contains(output, "Headless mode needs --event.");
            AssertUnchanged(before);
        }

        [TestMethod]
        public void Event_NotIdentifier_Fails()
        {
            var before = Snapshot();

            Assert.IsFalse(Run(EventGeneratorTool.CreateNewEvent, ("--event", "1st")).Succeeded);

            AssertUnchanged(before);
        }

        [TestMethod]
        public void Event_Existing_Refuses()
        {
            Run(EventGeneratorTool.CreateNewEvent, ("--event", "StatusEvent"));
            var before = Snapshot();

            var output = Capture(() => Assert.IsFalse(Run(EventGeneratorTool.CreateNewEvent, ("--event", "StatusEvent")).Succeeded));

            StringAssert.Contains(output, "already exists.");
            AssertUnchanged(before);
        }
    }
}
