using StatePipes.ServiceCreatorToolSetup;
using System.Diagnostics;

namespace StatePipes.Test.ServiceCreatorToolSetup
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ProcessCommandRunnerTest
    {
        [TestMethod]
        public void Run_CapturesOutputAndExitCode()
        {
            var result = new ProcessCommandRunner().Run("cmd.exe", ["/c", "echo", "hi", "&", "exit", "3"]);

            Assert.AreEqual(3, result.ExitCode);
            StringAssert.Contains(result.Output, "hi");
        }

        [TestMethod]
        [Timeout(60000, CooperativeCancellation = true)]
        public void Run_ChildStillHoldingOutput_ReturnsWithinTimeout()
        {
            // cmd exits at once, but the background powershell it started keeps the output pipe open for 15 s
            var watch = Stopwatch.StartNew();

            var result = new ProcessCommandRunner(TimeSpan.FromSeconds(3)).Run("cmd.exe",
                ["/c", "start", "/b", "powershell", "-NoProfile", "-Command", "Start-Sleep 15", "&", "echo", "hi"]);

            Assert.IsLessThan(10, watch.Elapsed.TotalSeconds, $"took {watch.Elapsed}");
            Assert.AreEqual(-1, result.ExitCode);
        }
    }
}
