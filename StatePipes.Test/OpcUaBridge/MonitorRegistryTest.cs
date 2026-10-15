using Microsoft.Extensions.Time.Testing;
using StatePipes.Comms;
using StatePipes.OpcUaBridge;
using static StatePipes.Test.OpcUaBridge.MonitorStoreTest;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class MonitorRegistryTest
    {
        /// <summary>The bridge's own BusConfig, matching the broker and cert of <see cref="MonitorStoreTest.Definition"/>.</summary>
        internal static readonly BusConfig Own = new("amqps://broker", "Line1Plc", "c.p12", "p.txt");

        private string _directory = string.Empty;
        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero));
        private string Path => System.IO.Path.Combine(_directory, "monitors.json");

        [TestInitialize]
        public void Initialize() => _directory = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MonitorRegistryTest", Guid.NewGuid().ToString("N"))).FullName;

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var file in Directory.GetFiles(_directory)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_directory, true);
        }

        private MonitorRegistry Load(TimeSpan? expiry = null) => MonitorRegistry.Load(new MonitorStore(Path), Own, expiry, _time);

        [TestMethod]
        public void Upsert_NewKey_ReturnsNullAndPersists()
        {
            var definition = Definition("a");
            Assert.IsNull(Load().Upsert(definition));
            Assert.AreEqual(definition, Load().Get(definition.Key));
        }

        [TestMethod]
        public void Upsert_SameKeyOtherNode_ReplacesDefinition()
        {
            var registry = Load();
            var first = Definition("a");
            registry.Upsert(first);
            var previous = registry.Upsert(Definition("a", "ns=2;s=msg"));
            Assert.AreEqual(first, previous);
            Assert.AreEqual("ns=2;s=msg", registry.Get(first.Key)!.NodeIdText);
            Assert.HasCount(1, registry.GetAll());
        }

        [TestMethod]
        public void Upsert_SameNameOtherSender_SeparateMonitors()
        {
            var registry = Load();
            registry.Upsert(Definition("a", sender: "Client1"));
            registry.Upsert(Definition("a", sender: "Client2"));
            Assert.HasCount(2, registry.GetAll());
        }

        [TestMethod]
        public void Upsert_Suspended_ClearsSuspension()
        {
            var registry = Load();
            var definition = Definition("a");
            registry.Upsert(definition);
            registry.Suspend(definition.Key);
            registry.Upsert(definition with { SuspendedSince = _time.GetUtcNow().UtcDateTime });
            Assert.IsNull(registry.Get(definition.Key)!.SuspendedSince);
        }

        [TestMethod]
        public void Suspend_ThenReload_SuspensionPersisted()
        {
            var registry = Load();
            var definition = Definition("a");
            registry.Upsert(definition);
            registry.Suspend(definition.Key);
            Assert.AreEqual(_time.GetUtcNow().UtcDateTime, Load().Get(definition.Key)!.SuspendedSince);
        }

        [TestMethod]
        public void Remove_ExistingAndMissing()
        {
            var registry = Load();
            var definition = Definition("a");
            registry.Upsert(definition);
            Assert.IsTrue(registry.Remove(definition.Key));
            Assert.IsFalse(registry.Remove(definition.Key));
            Assert.IsEmpty(Load().GetAll());
        }

        [TestMethod]
        public void RemoveExpired_PastExpiry_RemovesOnlyExpiredSuspended()
        {
            var registry = Load(TimeSpan.FromHours(2));
            MonitorDefinition a = Definition("a"), b = Definition("b"), c = Definition("c");
            registry.Upsert(a); registry.Upsert(b); registry.Upsert(c);
            registry.Suspend(a.Key);
            _time.Advance(TimeSpan.FromHours(3));
            registry.Suspend(b.Key);
            CollectionAssert.AreEqual(new[] { a.Key }, registry.RemoveExpired().ToArray());
            Assert.HasCount(2, Load().GetAll());
        }

        [TestMethod]
        public void RemoveExpired_NoExpiryConfigured_KeepsAll()
        {
            var registry = Load();
            var definition = Definition("a");
            registry.Upsert(definition);
            registry.Suspend(definition.Key);
            _time.Advance(TimeSpan.FromDays(3650));
            Assert.IsEmpty(registry.RemoveExpired());
        }

        [TestMethod]
        public void Load_ExpiredSuspended_Removed()
        {
            var registry = Load(TimeSpan.FromHours(1));
            var definition = Definition("a");
            registry.Upsert(definition);
            registry.Suspend(definition.Key);
            _time.Advance(TimeSpan.FromHours(2));
            Assert.IsEmpty(Load(TimeSpan.FromHours(1)).GetAll());
        }

        [TestMethod]
        public void Load_EntryFailingGuards_IsDropped()
        {
            var valid = Definition("valid");
            var otherBroker = Definition("broker") with { ReplyToRaw = valid.ReplyToRaw.Replace("amqps://broker", "amqps://elsewhere") };
            var otherCert = Definition("cert") with { ReplyToRaw = valid.ReplyToRaw.Replace("c.p12", "other.p12") };
            var garbage = Definition("garbage") with { ReplyToRaw = "not json" };
            new MonitorStore(Path).Save([valid, otherBroker, otherCert, garbage]);
            CollectionAssert.AreEqual(new[] { valid }, Load().GetAll().ToArray());
        }

        [TestMethod]
        public void Mutation_SaveFails_RegistryStillUpdated()
        {
            var registry = Load();
            registry.Upsert(Definition("a"));
            File.SetAttributes(Path, FileAttributes.ReadOnly);
            registry.Upsert(Definition("b"));
            Assert.HasCount(2, registry.GetAll());
            Assert.HasCount(1, new MonitorStore(Path).Load());
        }

        [TestMethod]
        public void ReplyToGuard_OtherBrokerOrCert_Refused()
        {
            Assert.IsTrue(ReplyToGuard.Allows(Own, new BusConfig("amqps://broker", "Client", "c.p12", "x"), out _));
            Assert.IsFalse(ReplyToGuard.Allows(Own, new BusConfig("amqps://other", "Client", "c.p12", "x"), out var brokerReason));
            StringAssert.Contains(brokerReason, "amqps://other");
            Assert.IsFalse(ReplyToGuard.Allows(Own, new BusConfig("amqps://broker", "Client", "d.p12", "x"), out var certReason));
            StringAssert.Contains(certReason, "d.p12");
        }
    }
}
