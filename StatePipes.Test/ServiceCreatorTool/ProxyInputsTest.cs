using StatePipes.ServiceCreatorTool;

namespace StatePipes.Test.ServiceCreatorTool
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ProxyInputsTest
    {
        [TestMethod]
        public void GetProxyMoniker_WithSuppliedMoniker_ReturnsItWithoutDialog()
        {
            var result = ProxyGeneratorTool.GetProxyMoniker(null, new ProxyInputs(Moniker: "Peer", Headless: true));

            Assert.AreEqual("Peer", result);
        }

        [TestMethod]
        public void GetProxyMoniker_WithUpdateFile_TakesMonikerFromFileName()
        {
            var result = ProxyGeneratorTool.GetProxyMoniker(@"C:\x\Proxies\PeerProxy.cs", new ProxyInputs(Headless: true));

            Assert.AreEqual("Peer", result);
        }

        [TestMethod]
        public void GetProxyMoniker_HeadlessWithoutMoniker_ReturnsEmptyWithoutDialog()
        {
            var result = ProxyGeneratorTool.GetProxyMoniker(null, new ProxyInputs(Headless: true));

            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void MissingInHeadless_OnlyWhenHeadlessAndValueMissing()
        {
            Assert.IsTrue(new ProxyInputs(Headless: true).MissingInHeadless(null, "--dll"));
            Assert.IsFalse(new ProxyInputs(Headless: true).MissingInHeadless("a.dll", "--dll"));
            Assert.IsFalse(new ProxyInputs().MissingInHeadless(null, "--dll"));
        }

        [TestMethod]
        public void WithStateMachineFromProxyFile_KeepsTheMachineTheExistingProxyTargets()
        {
            var proxy = Path.Combine(Path.GetTempPath(), $"PeerProxy_{Guid.NewGuid()}.cs");
            File.WriteAllText(proxy, "internal class DummyEventFromPeerTrigger: BaseTriggerCommand<Acme.Widgets.StateMachines.Valve.Valve>");
            try
            {
                Assert.AreEqual("Acme.Widgets.StateMachines.Valve.Valve", ProxyGeneratorTool.WithStateMachineFromProxyFile(new ProxyInputs(Headless: true), proxy)!.StateMachine);
                Assert.AreEqual("Pump", ProxyGeneratorTool.WithStateMachineFromProxyFile(new ProxyInputs(StateMachine: "Pump"), proxy)!.StateMachine);
                Assert.IsNull(ProxyGeneratorTool.WithStateMachineFromProxyFile(new ProxyInputs(), null)!.StateMachine);
            }
            finally { File.Delete(proxy); }
        }

        [TestMethod]
        public void HasLiveConnection_NeedsAllFourValues()
        {
            Assert.IsTrue(new ProxyInputs(BrokerUri: "amqps://b", ExchangeName: "x", CertPath: "c.p12", CertPasswordPath: "p.txt").HasLiveConnection);
            Assert.IsFalse(new ProxyInputs(BrokerUri: "amqps://b", ExchangeName: "x", CertPath: "c.p12").HasLiveConnection);
        }
    }
}
