using StatePipes.Test.ClaudePlugin;
using System.Diagnostics;

namespace StatePipes.Test.ServiceCreatorTool
{
    /// <summary>
    /// Runs the built ServiceCreatorTool and Diagrammer headless, as Claude does, on a new solution in a temp folder, building it
    /// with dotnet between the steps that read the service DLL. Needs the dotnet SDK and access to the StatePipes NuGet package.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class HeadlessEndToEndTest
    {
        private const string SolutionName = "Packages.Acme.E2e";
        private const string Prefix = "Acme.E2e";
        private string _root = null!;
        private string _solutionDir = null!;
        private string StatesDir => Path.Combine(_solutionDir, Prefix, "StateMachines", "Valve", "States");

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), $"HeadlessEndToEndTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

        /// <summary>The tools' own output folders, for the configuration these tests were built with.</summary>
        private static string ToolExe(string project)
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var exe = Path.Combine(PluginPaths.RepoRoot(), project, "bin", configuration, "net10.0-windows", $"{project}.exe");
            Assert.IsTrue(File.Exists(exe), exe);
            return exe;
        }

        private static (int ExitCode, string Output) Run(string fileName, string workingDirectory, params string[] args)
        {
            var info = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Assert.IsTrue(process.WaitForExit(TimeSpan.FromMinutes(5)), $"{fileName} timed out");
            return (process.ExitCode, stdout.Result + stderr.Result);
        }

        private string[] Common(params string[] more) =>
            ["-headless", "-r", _solutionDir + "\\", "-s", SolutionName + ".slnx", "-p", Prefix + ".csproj",
             "-b", Path.Combine(_solutionDir, Prefix, "obj", "Debug", "net10.0") + "\\", .. more];

        private (int ExitCode, string Output) Tool(params string[] args) => Run(ToolExe("StatePipes.ServiceCreatorTool"), Directory.Exists(_solutionDir ?? "") ? _solutionDir! : _root, args);

        private void Succeeds(params string[] args)
        {
            var (exitCode, output) = Tool(args);
            Assert.AreEqual(0, exitCode, $"{string.Join(" ", args)}\n{output}");
            StringAssert.Contains(output, "Finished!!!");
        }

        private void Build()
        {
            var (exitCode, output) = Run("dotnet", _solutionDir, "build", SolutionName + ".slnx", "-v", "q");
            Assert.AreEqual(0, exitCode, output);
        }

        private void CreateSolutionWithValve()
        {
            Succeeds("-headless", "--repo", _root, "--solution-name", SolutionName, "--transport", "rabbitmq");
            _solutionDir = Path.Combine(_root, SolutionName);
            // New solution already creates the first service, Acme.E2e; new service adds another
            Succeeds("-headless", "-r", _solutionDir + "\\", "-s", SolutionName + ".slnx", "--prefix", Prefix + ".Pump");
            Assert.IsTrue(File.Exists(Path.Combine(_solutionDir, Prefix + ".Pump", Prefix + ".Pump.csproj")));
            Succeeds(Common("-m", "--statemachine-name", "Valve"));
            Build();
        }

        private Dictionary<string, DateTime> Snapshot() =>
            Directory.GetFiles(_solutionDir, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.GetLastWriteTimeUtc);

        [TestMethod]
        public void HeadlessRun_EveryOperation_BuildsAndDiagrams()
        {
            CreateSolutionWithValve();
            Assert.IsTrue(File.Exists(Path.Combine(_solutionDir, Prefix, "StateMachines", "Valve", "Valve.cs")));

            Succeeds(Common("-t", "--statemachine", "Valve", "--trigger-name", "Open", "--trigger-scope", "public"));
            Succeeds(Common("-t", "--statemachine", "Valve", "--trigger-name", "Tick", "--trigger-scope", "internal"));
            Succeeds(Common("-a", "--statemachine", "Valve", "--state", "Closed"));
            Succeeds(Common("-a", "--statemachine", "Valve", "--state", "Opened"));
            Succeeds(Common("-e", "--event", "ValveOpenedEvent"));
            Assert.IsTrue(File.Exists(Path.Combine(_solutionDir, Prefix, "StateMachines", "Valve", "Triggers", "Internal", "Tick.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(_solutionDir, Prefix, "Events", "ValveOpenedEvent.cs")));
            Build();

            Succeeds(Common("-a", "--statemachine", "Valve", "--state", "Draining", "--parent-state", "Opened"));
            StringAssert.Contains(File.ReadAllText(Path.Combine(StatesDir, "Draining.cs")), "Opened");
            Build();

            // A state file path relative to the working directory (the solution folder)
            Succeeds(Common("-pi", Path.Combine(Prefix, "StateMachines", "Valve", "States", "Closed.cs"), "--trigger", "Open", "--transition-to", "Opened"));
            Succeeds(Common("-pri", Path.Combine(StatesDir, "Opened.cs"), "--trigger", "Tick"));
            Succeeds(Common("-ii", Path.Combine(StatesDir, "Closed.cs"), "--trigger", "Tick"));
            Succeeds(Common("-pti", Path.Combine(StatesDir, "Opened.cs"), "--timer-name", "Poll", "--timer-period", "500"));
            StringAssert.Contains(File.ReadAllText(Path.Combine(StatesDir, "Closed.cs")), "Opened");
            Build();

            var diagrams = Path.Combine(_root, "diagrams");
            var (exitCode, output) = Run(ToolExe("StatePipes.Diagrammer"), _solutionDir, "-headless", "-r", _solutionDir + "\\", "-s", SolutionName + ".slnx",
                "-p", Prefix + ".csproj", "-c", Path.Combine(_solutionDir, Prefix, "obj", "Debug", "net10.0") + "\\", "--out", diagrams);
            Assert.AreEqual(0, exitCode, output);
            StringAssert.Contains(output, "Finished!!!");
            Assert.IsNotEmpty(Directory.GetFiles(diagrams, "*.pdf"), output);
        }

        [TestMethod]
        public void HeadlessRun_MissingArgument_FailsWithoutChanges()
        {
            CreateSolutionWithValve();
            Succeeds(Common("-t", "--statemachine", "Valve", "--trigger-name", "Open", "--trigger-scope", "public"));
            Succeeds(Common("-a", "--statemachine", "Valve", "--state", "Closed"));
            Build();
            var closed = Path.Combine(StatesDir, "Closed.cs");
            string[][] runs =
            [
                Common("-m"),
                Common("-a", "--statemachine", "Valve"),
                Common("-t", "--statemachine", "Valve", "--trigger-scope", "public"),
                Common("-e"),
                Common("-pi", closed, "--trigger", "Open"),
                Common("-pri", closed),
                Common("-ii", closed),
                Common("-pti", closed, "--timer-name", "Poll"),
                ["-headless", "-r", _solutionDir + "\\", "-s", SolutionName + ".slnx"],
            ];
            var before = Snapshot();

            foreach (var args in runs)
            {
                var (exitCode, output) = Tool(args);
                Assert.AreEqual(1, exitCode, $"{string.Join(" ", args)}\n{output}");
                StringAssert.Contains(output, "Headless mode needs", string.Join(" ", args));
                Assert.IsFalse(output.Contains("Finished!!!"), output);
            }

            CollectionAssert.AreEquivalent(before.ToList(), Snapshot().ToList(), "files changed");
        }

        [TestMethod]
        public void HeadlessRun_ExistingState_Refuses()
        {
            CreateSolutionWithValve();
            Succeeds(Common("-a", "--statemachine", "Valve", "--state", "Closed"));
            var closed = Path.Combine(StatesDir, "Closed.cs");
            var text = File.ReadAllText(closed);

            var (exitCode, output) = Tool(Common("-a", "--statemachine", "Valve", "--state", "Closed"));

            Assert.AreEqual(1, exitCode, output);
            StringAssert.Contains(output, "already exists.");
            Assert.AreEqual(text, File.ReadAllText(closed));
        }
    }
}
