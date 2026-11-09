using StatePipes.ServiceCreatorTool;
using StatePipes.Test.ClaudePlugin;
using System.Diagnostics;
using static StatePipes.Test.ServiceCreatorTool.ToolTestHelpers;

namespace StatePipes.Test.ServiceCreatorTool
{
    /// <summary>The -w Add Workflow operation, run headless on a freshly generated solution.</summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowGeneratorToolTest
    {
        internal const string SolutionName = "Packages.Acme.Shop";
        internal const string Prefix = "Acme.Shop";
        private const string CertCopy = @"if exist $(ProgramData)\SmallStep\step-ca\statepipes.postgres-db.client.p12 copy $(ProgramData)\SmallStep\step-ca\statepipes.postgres-db.client.p12 $(OutDir)\certs\statepipes.postgres-db.client.p12";
        private string _root = null!;
        internal string SolutionDir = null!;
        internal string ProjectDir => Path.Combine(SolutionDir, Prefix);
        private string ConfigPath => Path.Combine(ProjectDir, "Builders", "DefaultServiceConfiguration.cs");
        private string ServiceCsproj => Path.Combine(SolutionDir, Prefix + ".Service", Prefix + ".Service.csproj");

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), $"WorkflowGeneratorToolTest_{Guid.NewGuid()}");
            SolutionDir = Path.Combine(_root, SolutionName);
            new SolutionGeneratorTool(SolutionDir, SolutionName + BaseToolGenerator.solutionExtension, usingRabbit: true).GenerateSolution();
        }

        [TestCleanup]
        public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

        internal ToolResult AddWorkflow(string name) =>
            WorkflowGeneratorTool.CreateNewWorkflow(SolutionDir, SolutionName + BaseToolGenerator.solutionExtension, Prefix + ".csproj",
                Path.Combine(ProjectDir, "obj", "Debug", "net10.0"), Headless(("--workflow-name", name)));

        private static int Count(string text, string value) => (text.Length - text.Replace(value, "").Length) / value.Length;

        [TestMethod]
        public void Workflow_Headless_CreatesMarkerTopLevelAndFinished()
        {
            Assert.IsTrue(AddWorkflow("Cart").Succeeded);
            var dir = Path.Combine(ProjectDir, "StateMachines", "Cart");
            StringAssert.Contains(File.ReadAllText(Path.Combine(dir, "Cart.cs")), "public interface Cart : IWorkflow");
            StringAssert.Contains(File.ReadAllText(Path.Combine(dir, "States", "TopLevelState.cs")), "BaseWorkflowState<Cart>, IFirstStateForStateMachine");
            StringAssert.Contains(File.ReadAllText(Path.Combine(dir, "States", "FinishedState.cs")), "BaseWorkflowState<Cart>, IFinalWorkflowState");
        }

        [TestMethod]
        public void Workflow_Existing_RefusesAndWritesNothing()
        {
            AddWorkflow("Cart");
            var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.GetLastWriteTimeUtc);
            var output = Capture(() => Assert.IsFalse(AddWorkflow("Cart").Succeeded));
            StringAssert.Contains(output, "already exists");
            CollectionAssert.AreEquivalent(before.ToList(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.GetLastWriteTimeUtc).ToList());
        }

        [TestMethod]
        public void NewService_TemplateDoesNotReferenceWorkflowStore() =>
            Assert.IsFalse(File.ReadAllText(ConfigPath).Contains("WorkflowStore"), "services without workflows must not depend on the workflow API");

        [TestMethod]
        public void Workflow_InsertsStoreConfigAndCertCopy()
        {
            Assert.IsTrue(AddWorkflow("Cart").Succeeded);
            var config = File.ReadAllText(ConfigPath);
            StringAssert.Contains(config, "WorkflowStoreConfig? workflowStore = new WorkflowStoreConfig(\"postgres-db\", \"statepipes\", \"statepipes\", \"statepipes.postgres-db.client.p12\");");
            StringAssert.Contains(config, "new ServiceArgs(null), workflowStore)");
            Assert.IsTrue(config.IndexOf("workflowStore = new", StringComparison.Ordinal) < config.IndexOf("Type setup", StringComparison.Ordinal));
            StringAssert.Contains(File.ReadAllText(ServiceCsproj), CertCopy);
        }

        [TestMethod]
        public void Workflow_UnrecognizedConfig_FailsAndWritesNothing()
        {
            File.WriteAllText(ConfigPath, "// hand-written configuration");
            var output = Capture(() => Assert.IsFalse(AddWorkflow("Cart").Succeeded));
            StringAssert.Contains(output, "DefaultServiceConfiguration.cs");
            Assert.IsFalse(Directory.Exists(Path.Combine(ProjectDir, "StateMachines", "Cart")));
        }

        [TestMethod]
        public void Workflow_SecondWorkflow_InsertionsIdempotent()
        {
            Assert.IsTrue(AddWorkflow("Cart").Succeeded);
            Assert.IsTrue(AddWorkflow("Order").Succeeded);
            Assert.AreEqual(1, Count(File.ReadAllText(ConfigPath), "new WorkflowStoreConfig("));
            Assert.AreEqual(1, Count(File.ReadAllText(ServiceCsproj), "statepipes.postgres-db.client.p12 copy"));
        }
    }

    /// <summary>Generated workflow code compiles against this repository's StatePipes (not the published package).</summary>
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class WorkflowGeneratedSolutionBuildTest
    {
        internal static void BuildClassLibraryAgainstRepo(string projectDir, string prefix)
        {
            var csproj = Path.Combine(projectDir, prefix + ".csproj");
            var text = File.ReadAllText(csproj);
            var start = text.IndexOf("<PackageReference Include=\"StatePipes\"", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "StatePipes package reference not found");
            var end = text.IndexOf("/>", start, StringComparison.Ordinal) + 2;
            var repoStatePipes = Path.Combine(PluginPaths.RepoRoot(), "StatePipes", "StatePipes.csproj");
            File.WriteAllText(csproj, text[..start] + $"<ProjectReference Include=\"{repoStatePipes}\" />" + text[end..]);
            var info = new ProcessStartInfo("dotnet", ["build", csproj, "-nologo", "-v", "q"]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.IsTrue(process.WaitForExit(TimeSpan.FromMinutes(5)), "dotnet build timed out");
            Assert.AreEqual(0, process.ExitCode, output);
        }

        [TestMethod]
        public void Workflow_GeneratedSolution_Builds()
        {
            var t = new WorkflowGeneratorToolTest();
            t.Setup();
            try
            {
                Assert.IsTrue(t.AddWorkflow("Cart").Succeeded);
                BuildClassLibraryAgainstRepo(t.ProjectDir, WorkflowGeneratorToolTest.Prefix);
            }
            finally { t.Cleanup(); }
        }
    }
}
