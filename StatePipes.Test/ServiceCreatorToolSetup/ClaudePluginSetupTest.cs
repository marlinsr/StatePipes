using StatePipes.ServiceCreatorToolSetup;

namespace StatePipes.Test.ServiceCreatorToolSetup
{
    /// <summary>Answers claude commands by their argument string and records each one, so no real Claude Code runs.</summary>
    internal sealed class FakeRunner : ICommandRunner
    {
        public Dictionary<string, CommandResult> Answers { get; } = [];
        public List<string> Ran { get; } = [];
        public CommandResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            var key = string.Join(" ", arguments);
            Ran.Add(key);
            return Answers.TryGetValue(key, out var result) ? result : new CommandResult(0, "");
        }
    }

    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ClaudePluginSetupTest
    {
        private const string ListMarketplaces = "plugin marketplace list --json";
        private const string ListPlugins = "plugin list --json";
        private const string Install = "plugin install statepipes@statepipes";
        private const string Uninstall = "plugin uninstall statepipes@statepipes";
        private const string RemoveMarketplace = "plugin marketplace remove statepipes";
        private const string GitHubMarketplace = """[{"name":"statepipes","source":"github","repo":"marlinsr/StatePipes"}]""";
        private const string InstalledPlugin = """[{"id":"statepipes@statepipes","version":"6.0.4"}]""";
        private string _dir = string.Empty;
        private readonly FakeRunner _runner = new();
        private readonly StringWriter _output = new();
        private string AddMarketplace => $"plugin marketplace add {_dir}";
        private string LocalMarketplace(string path) =>
            $$"""[{"name":"statepipes","source":"directory","path":{{System.Text.Json.JsonSerializer.Serialize(path)}}}]""";
        private ClaudePluginSetup Setup(string? claudePath = @"C:\bin\claude.exe") => new(_runner, claudePath, _dir, _output);

        [TestInitialize]
        public void Init()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"ClaudePluginSetupTest_{Guid.NewGuid():N}", "ClaudePlugin");
            Directory.CreateDirectory(Path.Combine(_dir, "statepipes", ".claude-plugin"));
            File.WriteAllText(Path.Combine(_dir, "statepipes", ".claude-plugin", "plugin.json"), """{"name":"statepipes","version":"6.1.0"}""");
        }

        [TestCleanup]
        public void Cleanup() => Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true);

        private void AssertManualCommands()
        {
            StringAssert.Contains(_output.ToString(), $"claude plugin marketplace add \"{_dir}\"");
            StringAssert.Contains(_output.ToString(), "claude plugin install statepipes@statepipes");
        }

        [TestMethod]
        public void Register_ClaudeNotFound_PrintsManualCommands()
        {
            Setup(claudePath: null).Register();

            StringAssert.Contains(_output.ToString(), "Claude Code not found; to add the StatePipes plugin later, run:");
            AssertManualCommands();
            Assert.AreEqual(0, _runner.Ran.Count);
        }

        [TestMethod]
        public void Register_NoMarketplace_AddsAndInstalls()
        {
            _runner.Answers[ListMarketplaces] = new(0, "[]");

            Setup().Register();

            CollectionAssert.AreEqual(new[] { ListMarketplaces, AddMarketplace, Install }, _runner.Ran);
            StringAssert.Contains(_output.ToString(), "StatePipes Claude Code plugin 6.1.0 installed.");
        }

        [TestMethod]
        public void Register_GitHubMarketplace_SwitchesToLocal()
        {
            _runner.Answers[ListMarketplaces] = new(0, GitHubMarketplace);
            _runner.Answers[ListPlugins] = new(0, InstalledPlugin);

            Setup().Register();

            CollectionAssert.AreEqual(new[] { ListMarketplaces, ListPlugins, Uninstall, RemoveMarketplace, AddMarketplace, Install }, _runner.Ran);
        }

        [TestMethod]
        public void Register_AlreadyLocal_InstallsWithoutReAdding()
        {
            _runner.Answers[ListMarketplaces] = new(0, LocalMarketplace(_dir.ToLowerInvariant() + "\\"));

            Setup().Register();

            CollectionAssert.AreEqual(new[] { ListMarketplaces, Install }, _runner.Ran);
        }

        [TestMethod]
        public void Register_FailingCommand_PrintsReasonAndManualCommandsAndStops()
        {
            _runner.Answers[ListMarketplaces] = new(0, "[]");
            _runner.Answers[AddMarketplace] = new(1, "boom");

            Setup().Register();

            StringAssert.Contains(_output.ToString(), "Claude Code plugin setup failed: claude plugin marketplace add");
            StringAssert.Contains(_output.ToString(), "boom");
            AssertManualCommands();
            CollectionAssert.DoesNotContain(_runner.Ran, Install);
        }

        [TestMethod]
        public void Register_ListNotJson_TreatedAsFailure()
        {
            _runner.Answers[ListMarketplaces] = new(0, "not json");

            Setup().Register();

            StringAssert.Contains(_output.ToString(), "Claude Code plugin setup failed: claude plugin marketplace list --json");
            AssertManualCommands();
            Assert.AreEqual(1, _runner.Ran.Count);
        }

        [TestMethod]
        public void Remove_LocalRegistration_UninstallsAndRemoves()
        {
            _runner.Answers[ListMarketplaces] = new(0, LocalMarketplace(_dir));
            _runner.Answers[ListPlugins] = new(0, InstalledPlugin);

            Setup().Remove();

            CollectionAssert.AreEqual(new[] { ListMarketplaces, ListPlugins, Uninstall, RemoveMarketplace }, _runner.Ran);
            StringAssert.Contains(_output.ToString(), "StatePipes plugin removed from Claude Code.");
        }

        [TestMethod]
        public void Remove_GitHubRegistration_LeavesItAlone()
        {
            _runner.Answers[ListMarketplaces] = new(0, GitHubMarketplace);

            Setup().Remove();

            CollectionAssert.AreEqual(new[] { ListMarketplaces }, _runner.Ran);
        }

        [TestMethod]
        public void Remove_ClaudeNotFound_PrintsAndReturns()
        {
            Setup(claudePath: null).Remove();

            StringAssert.Contains(_output.ToString(), "Claude Code not found; StatePipes plugin not removed.");
            Assert.AreEqual(0, _runner.Ran.Count);
        }
    }
}
