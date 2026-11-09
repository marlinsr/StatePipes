using StatePipes.ServiceCreatorTool;
using StatePipes.Test.ClaudePlugin;
using System.Diagnostics;
using static StatePipes.Test.ServiceCreatorTool.ToolTestHelpers;

namespace StatePipes.Test.ServiceCreatorTool
{
    /// <summary>Workflow variants of the state, trigger and periodic-timer operations that do not read the built DLL.</summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowStateTriggerToolTest
    {
        private readonly WorkflowGeneratorToolTest _solution = new();
        private string ProjectDir => _solution.ProjectDir;
        private string CartStates => Path.Combine(ProjectDir, "StateMachines", "Cart", "States");

        [TestInitialize]
        public void Setup()
        {
            _solution.Setup();
            Assert.IsTrue(_solution.AddWorkflow("Cart").Succeeded);
        }

        [TestCleanup]
        public void Cleanup() => _solution.Cleanup();

        private ToolResult Periodic(string stateFile) =>
            PeriodicTriggerGeneratorTool.CreatePeriodicTrigger(_solution.SolutionDir, WorkflowGeneratorToolTest.SolutionName + BaseToolGenerator.solutionExtension,
                WorkflowGeneratorToolTest.Prefix + ".csproj", Path.Combine(ProjectDir, "obj", "Debug", "net10.0"), stateFile,
                Headless(("--timer-name", "abandonTimer"), ("--timer-period", "1000")));

        [TestMethod]
        public void StateMachineName_FromWorkflowStateSource() =>
            Assert.AreEqual("Cart", StateMachineTypesHelper.GetStateMachineNameFromSourceFile(Path.Combine(CartStates, "TopLevelState.cs")));

        [TestMethod]
        public void IsWorkflow_DistinguishesWorkflowFromSingleton()
        {
            Assert.IsTrue(StateMachineGeneratorTool.CreateNewStateMachine(_solution.SolutionDir, WorkflowGeneratorToolTest.SolutionName + BaseToolGenerator.solutionExtension,
                WorkflowGeneratorToolTest.Prefix + ".csproj", Path.Combine(ProjectDir, "obj", "Debug", "net10.0"), Headless(("--statemachine-name", "Valve"))).Succeeded);
            Assert.IsTrue(StateMachineTypesHelper.IsWorkflow(ProjectDir, "Cart"));
            Assert.IsFalse(StateMachineTypesHelper.IsWorkflow(ProjectDir, "Valve"));
        }

        [TestMethod]
        public void Periodic_OnWorkflow_StampsCorrelationIdAndUsesWorkflowTrigger()
        {
            Assert.IsTrue(Periodic(Path.Combine(CartStates, "TopLevelState.cs")).Succeeded);
            StringAssert.Contains(File.ReadAllText(Path.Combine(CartStates, "TopLevelState.cs")),
                "abandonTimer?.StartPeriodic(TimeSpan.FromMilliseconds(1000), new AbandonTimerTrigger() { CorrelationId = CorrelationId });");
            StringAssert.Contains(File.ReadAllText(Path.Combine(ProjectDir, "StateMachines", "Cart", "Triggers", "Internal", "AbandonTimerTrigger.cs")),
                ": BaseWorkflowTriggerCommand<Cart>");
        }

        [TestMethod]
        public void Periodic_OnSingleton_Unchanged()
        {
            StateMachineGeneratorTool.CreateNewStateMachine(_solution.SolutionDir, WorkflowGeneratorToolTest.SolutionName + BaseToolGenerator.solutionExtension,
                WorkflowGeneratorToolTest.Prefix + ".csproj", Path.Combine(ProjectDir, "obj", "Debug", "net10.0"), Headless(("--statemachine-name", "Valve")));
            var state = Path.Combine(ProjectDir, "StateMachines", "Valve", "States", "TopLevelState.cs");
            Assert.IsTrue(Periodic(state).Succeeded);
            StringAssert.Contains(File.ReadAllText(state), "new AbandonTimerTrigger());");
            StringAssert.Contains(File.ReadAllText(Path.Combine(ProjectDir, "StateMachines", "Valve", "Triggers", "Internal", "AbandonTimerTrigger.cs")),
                ": BaseTriggerCommand<Valve>");
        }

        [TestMethod]
        public void ClaudeMd_ContainsWorkflowsSection()
        {
            var text = ClaudeMdWriter.Render("Packages.Acme.Shop", "6.1.0");
            StringAssert.Contains(text, "## Workflows");
            StringAssert.Contains(text, "parameterless constructor");
            StringAssert.Contains(text, "IFinalWorkflowState");
        }
    }

    /// <summary>
    /// The workflow variants of -a (with --final), -t and -pi, which read the built service DLL: the generated service is
    /// built against this repository's StatePipes, the tool runs headless out of process, and the result must build.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class WorkflowToolsEndToEndTest
    {
        private static string ToolExe()
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            return Path.Combine(PluginPaths.RepoRoot(), "StatePipes.ServiceCreatorTool", "bin", configuration, "net10.0-windows", "StatePipes.ServiceCreatorTool.exe");
        }

        internal static (int ExitCode, string Output) Run(string fileName, string workingDirectory, params string[] args)
        {
            var info = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Assert.IsTrue(process.WaitForExit(TimeSpan.FromMinutes(5)), $"{fileName} timed out");
            return (process.ExitCode, stdout.Result + stderr.Result);
        }

        internal static void UseRepoStatePipes(string csproj)
        {
            var text = File.ReadAllText(csproj);
            var start = text.IndexOf("<PackageReference Include=\"StatePipes\"", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"StatePipes package reference not found in {csproj}");
            var end = text.IndexOf("/>", start, StringComparison.Ordinal) + 2;
            var repoStatePipes = Path.Combine(PluginPaths.RepoRoot(), "StatePipes", "StatePipes.csproj");
            File.WriteAllText(csproj, text[..start] + $"<ProjectReference Include=\"{repoStatePipes}\" />" + text[end..]);
        }

        internal static void Build(string csproj)
        {
            var (exitCode, output) = Run("dotnet", Path.GetDirectoryName(csproj)!, "build", csproj, "-nologo", "-v", "q");
            Assert.AreEqual(0, exitCode, output);
        }

        [TestMethod]
        public void WorkflowStateTriggerAndGuard_GenerateAndBuild()
        {
            var t = new WorkflowGeneratorToolTest();
            t.Setup();
            try
            {
                Assert.IsTrue(t.AddWorkflow("Cart").Succeeded);
                var prefix = WorkflowGeneratorToolTest.Prefix;
                var serviceCsproj = Path.Combine(t.SolutionDir, prefix + ".Service", prefix + ".Service.csproj");
                UseRepoStatePipes(Path.Combine(t.ProjectDir, prefix + ".csproj"));
                UseRepoStatePipes(serviceCsproj);
                string[] Common(params string[] more) =>
                    ["-headless", "-r", t.SolutionDir + "\\", "-s", WorkflowGeneratorToolTest.SolutionName + ".slnx", "-p", prefix + ".csproj",
                     "-b", Path.Combine(t.ProjectDir, "obj", "Debug", "net10.0") + "\\", .. more];
                void Tool(params string[] args)
                {
                    var (exitCode, output) = Run(ToolExe(), t.SolutionDir, args);
                    Assert.AreEqual(0, exitCode, $"{string.Join(" ", args)}\n{output}");
                }
                var states = Path.Combine(t.ProjectDir, "StateMachines", "Cart", "States");

                Build(serviceCsproj);
                Tool(Common("-a", "--statemachine", "Cart", "--state", "PaidState", "--final", "true"));
                Tool(Common("-t", "--statemachine", "Cart", "--trigger-scope", "Public", "--trigger-name", "Pay"));
                StringAssert.Contains(File.ReadAllText(Path.Combine(states, "PaidState.cs")), "BaseWorkflowState<Cart>, IFinalWorkflowState");
                StringAssert.Contains(File.ReadAllText(Path.Combine(t.ProjectDir, "StateMachines", "Cart", "Triggers", "Pay.cs")), ": BaseWorkflowTriggerCommand<Cart>");

                Build(serviceCsproj);
                Tool(Common("-pi", Path.Combine(states, "TopLevelState.cs"), "--trigger", "Pay", "--transition-to", "PaidState"));
                StringAssert.Contains(File.ReadAllText(Path.Combine(states, "TopLevelState.cs")), "[PermitIf(typeof(PaidState))]");
                Build(serviceCsproj);
            }
            finally { t.Cleanup(); }
        }
    }
}
