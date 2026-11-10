using System.Diagnostics;

namespace StatePipes.Test.ClaudePlugin
{
    /// <summary>The workflow rules of Check-StatePipesService.ps1, run static-only against small generated fixtures.</summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class VerifyScriptWorkflowTest
    {
        private string _root = null!;

        [TestInitialize]
        public void Setup() => _root = Path.Combine(Path.GetTempPath(), $"VerifyScriptWorkflowTest_{Guid.NewGuid():N}");

        [TestCleanup]
        public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

        private void Write(string relative, string contents)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        private (int ExitCode, string Output) Check()
        {
            var script = Path.Combine(PluginPaths.Skills, "statepipes-verify", "scripts", "Check-StatePipesService.ps1");
            var info = new ProcessStartInfo("pwsh", ["-NoProfile", "-File", script, "-SolutionDir", _root, "-SkipFormat", "-SkipBuildAndTest"])
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(info)!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output);
        }

        private void Common()
        {
            Write("Shop.slnx", "<Solution />");
            Write("Acme.Shop/StateMachines/Cart/Cart.cs", "public interface Cart : IWorkflow { }");
            Write("Acme.Shop/StateMachines/Valve/Valve.cs", "public interface Valve : IStateMachine { }");
            Write("Acme.Shop/StateMachines/Valve/States/OpenState.cs",
                "internal class OpenState : BaseStateMachineState<Valve> { public OpenState(IClock clock) { } void X() { FireExternal<Pump, Go>(new Go()); } }");
        }

        private static int Count(string text, string value) => (text.Length - text.Replace(value, "").Length) / value.Length;

        [TestMethod]
        public void GoodWorkflow_Passes()
        {
            Common();
            Write("Acme.Shop/StateMachines/Cart/States/TopLevelState.cs",
                "internal class TopLevelState : BaseWorkflowState<Cart>, IFirstStateForStateMachine { public TopLevelState() { } }");
            Write("Acme.Shop/StateMachines/Cart/States/FinishedState.cs",
                "internal class FinishedState : BaseWorkflowState<Cart>, IFinalWorkflowState { }");
            Write("Acme.Shop.Test/CartTest.cs", "public class CartTest { void T() { using var wf = new WorkflowForTest<Cart>(); } }");

            var (exitCode, output) = Check();
            Assert.AreEqual(0, exitCode, output);
            Assert.IsFalse(output.Contains("workflow-"), output);
            StringAssert.Contains(output, "PASS");
        }

        [TestMethod]
        public void BadWorkflow_ReportsEachRuleOnce()
        {
            Common();
            Write("Acme.Shop/StateMachines/Cart/States/TopLevelState.cs",
                "internal class TopLevelState : BaseWorkflowState<Cart>, IFirstStateForStateMachine { public TopLevelState(IPricing pricing) { } }");
            Write("Acme.Shop/StateMachines/Cart/States/PricingState.cs",
                "internal class PricingState : ParentedBaseWorkflowState<Cart, TopLevelState> { void X() { FireExternal<Pricing, Quote>(new Quote()); } }");

            var (exitCode, output) = Check();
            Assert.AreEqual(1, exitCode, output);
            foreach (var rule in new[] { "FAIL workflow-ctor:", "FAIL workflow-final:", "FAIL workflow-fireexternal:", "FAIL workflow-test:" })
                Assert.AreEqual(1, Count(output, rule), $"{rule}\n{output}");
            StringAssert.Contains(output, "TopLevelState");
            Assert.IsFalse(output.Contains("Valve"), "singleton state machines are not subject to workflow rules\n" + output);
        }
    }
}
