using StatePipes.ServiceCreatorTool;
using static StatePipes.Test.ServiceCreatorTool.ToolTestHelpers;

namespace StatePipes.Test.ServiceCreatorTool
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ProxyToolInputTest
    {
        [TestMethod]
        public void GetProxyMoniker_WithSuppliedMoniker_ReturnsItWithoutDialog() =>
            Assert.AreEqual("Peer", ProxyGeneratorTool.GetProxyMoniker(null, Headless(("--moniker", "Peer"))));

        [TestMethod]
        public void GetProxyMoniker_WithUpdateFile_TakesMonikerFromFileName()
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            var proxy = Path.Combine(dir, "PeerProxy.cs");
            File.WriteAllText(proxy, "");
            try { Assert.AreEqual("Peer", ProxyGeneratorTool.GetProxyMoniker(proxy, Headless())); }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [TestMethod]
        public void GetProxyMoniker_MissingUpdateFile_FailsAndNamesIt()
        {
            // Otherwise -u silently loses the existing proxy's state machine
            var missing = Path.Combine(Path.GetTempPath(), $"Missing_{Guid.NewGuid()}", "PeerProxy.cs");

            var output = Capture(() => Assert.IsNull(ProxyGeneratorTool.GetProxyMoniker(missing, Headless())));

            StringAssert.Contains(output, $"Proxy file not found: {missing}");
        }

        [TestMethod]
        public void GetProxyMoniker_HeadlessWithoutMoniker_IsNullAndNamesTheArgument()
        {
            var output = Capture(() => Assert.IsNull(ProxyGeneratorTool.GetProxyMoniker(null, Headless())));

            StringAssert.Contains(output, "Headless mode needs --moniker.");
        }

        [TestMethod]
        public void WithStateMachineFromProxyFile_KeepsTheMachineTheExistingProxyTargets()
        {
            var proxy = Path.Combine(Path.GetTempPath(), $"PeerProxy_{Guid.NewGuid()}.cs");
            File.WriteAllText(proxy, "internal class DummyEventFromPeerTrigger: BaseTriggerCommand<Acme.Widgets.StateMachines.Valve.Valve>");
            try
            {
                Assert.AreEqual("Valve", ProxyGeneratorTool.WithStateMachineFromProxyFile(Headless(), proxy).Text("--statemachine", "p"));
                Assert.AreEqual("Pump", ProxyGeneratorTool.WithStateMachineFromProxyFile(Headless(("--statemachine", "Pump")), proxy).Text("--statemachine", "p"));
                Assert.IsFalse(ProxyGeneratorTool.WithStateMachineFromProxyFile(Headless(), null).Supplied("--statemachine"));
            }
            finally { File.Delete(proxy); }
        }
    }

    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ProgramExitCodeTest
    {
        private const string SolutionName = "Packages.Acme.Widgets";
        private string _root = null!;
        private string _solutionDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), $"ProgramExitCodeTest_{Guid.NewGuid()}");
            _solutionDir = Path.Combine(_root, SolutionName);
            new SolutionGeneratorTool(_solutionDir, SolutionName + BaseToolGenerator.solutionExtension, usingRabbit: true).GenerateSolution();
        }

        [TestCleanup]
        public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

        private string[] Common(params string[] more) =>
            ["-headless", "-r", _solutionDir + "\\", "-s", SolutionName + ".slnx", "-p", "Acme.Widgets.csproj",
             "-b", Path.Combine(_solutionDir, "Acme.Widgets", "obj", "Debug", "net10.0") + "\\", .. more];

        [TestMethod]
        public void Main_HeadlessProxyMissingMoniker_ExitsOneWithoutFinished()
        {
            var exitCode = 0;
            var output = Capture(() => exitCode = StatePipes.ServiceCreatorTool.Program.Main(Common("--dll", Path.Combine(_solutionDir, "SolutionInfo.proj"))));

            Assert.AreEqual(1, exitCode, output);
            StringAssert.Contains(output, "Headless mode needs --moniker.");
            Assert.IsFalse(output.Contains("Finished!!!"), output);
        }

        [TestMethod]
        public void Main_UnknownArgument_ExitsOne()
        {
            var exitCode = 0;
            var output = Capture(() => exitCode = StatePipes.ServiceCreatorTool.Program.Main(Common("--colour", "red")));

            Assert.AreEqual(1, exitCode, output);
            StringAssert.Contains(output, "Unknown argument --colour.");
        }

        [TestMethod]
        public void Main_NoMatchingOperation_ExitsOne()
        {
            var exitCode = 0;
            var output = Capture(() => exitCode = StatePipes.ServiceCreatorTool.Program.Main(["-headless", "-r", _solutionDir + "\\", "-p", "Acme.Widgets.csproj"]));

            Assert.AreEqual(1, exitCode, output);
            StringAssert.Contains(output, "Incorrect Parameters Specified!");
        }
    }
}
