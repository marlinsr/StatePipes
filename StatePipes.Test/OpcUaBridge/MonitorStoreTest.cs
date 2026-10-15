using Newtonsoft.Json.Linq;
using StatePipes.OpcUaBridge;
using StatePipes.OpcUaBridge.Messages;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class MonitorStoreTest
    {
        private string _directory = string.Empty;
        private string Path => System.IO.Path.Combine(_directory, "monitors.json");

        [TestInitialize]
        public void Initialize() => _directory = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MonitorStoreTest", Guid.NewGuid().ToString("N"))).FullName;

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var file in Directory.GetFiles(_directory)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_directory, true);
        }

        internal static MonitorDefinition Definition(string name, string nodeId = "ns=2;s=pressure", DateTime? suspendedSince = null, string sender = "Client1") =>
            new(new MonitorKey($"{sender}.commands", name), nodeId,
                new MonitorParameters(MonitoringModeKind.Reporting, DataChangeTriggerKind.StatusValue, DeadbandTypeKind.Absolute, 0.5, 100, 2, 1000),
                $"{{\"BrokerUri\":\"amqps://broker\",\"ExchangeNamePrefix\":\"{sender}\",\"ClientCertPath\":\"c.p12\",\"ClientCertPasswordPath\":\"p.txt\",\"ResponseExchangeGuid\":\"abc\",\"ExchangeNamePostfix\":\"\"}}",
                suspendedSince);

        [TestMethod]
        public void SaveThenLoad_RoundTripsEveryField()
        {
            MonitorStore store = new(Path);
            MonitorDefinition[] saved = [Definition("a"), Definition("b", "ns=2;s=msg", new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc))];
            Assert.IsTrue(store.Save(saved));
            var loaded = new MonitorStore(Path).Load();
            CollectionAssert.AreEqual(saved, loaded.ToArray());
            Assert.AreEqual(DateTimeKind.Utc, loaded[1].SuspendedSince!.Value.Kind);
            Assert.AreEqual("abc", loaded[0].ReplyTo!.ResponseExchangeGuid);
        }

        [TestMethod]
        public void Save_WritesSpecFormat()
        {
            new MonitorStore(Path).Save([Definition("a")]);
            var json = JObject.Parse(File.ReadAllText(Path));
            Assert.AreEqual(1, (int)json["version"]!);
            var monitor = json["monitors"]![0]!;
            Assert.AreEqual("Client1.commands", (string)monitor["senderCommandExchange"]!);
            Assert.AreEqual("a", (string)monitor["monitorName"]!);
            Assert.AreEqual("ns=2;s=pressure", (string)monitor["nodeId"]!);
            Assert.AreEqual("Reporting", (string)monitor["monitoringMode"]!);
            Assert.AreEqual("StatusValue", (string)monitor["dataChangeTrigger"]!);
            Assert.AreEqual("Absolute", (string)monitor["deadbandType"]!);
            Assert.AreEqual(0.5, (double)monitor["deadbandValue"]!);
            Assert.AreEqual(100.0, (double)monitor["samplingInterval"]!);
            Assert.AreEqual(2u, (uint)monitor["queueSize"]!);
            Assert.AreEqual(1000.0, (double)monitor["publishingInterval"]!);
            Assert.AreEqual(JTokenType.String, monitor["replyTo"]!.Type);
            Assert.AreEqual(JTokenType.Null, monitor["suspendedSince"]!.Type);
        }

        [TestMethod]
        public void Load_MissingFile_Empty()
        {
            Assert.IsEmpty(new MonitorStore(Path).Load());
            Assert.IsFalse(File.Exists(Path));
        }

        [TestMethod]
        public void Load_CorruptFile_RenamedAndEmpty() => AssertRenamedAsCorrupt("{oops");

        [TestMethod]
        public void Load_UnknownVersion_TreatedAsCorrupt() => AssertRenamedAsCorrupt("{\"version\":2,\"monitors\":[]}");

        private void AssertRenamedAsCorrupt(string content)
        {
            File.WriteAllText(Path, content);
            Assert.IsEmpty(new MonitorStore(Path).Load());
            Assert.IsFalse(File.Exists(Path));
            var corrupt = Directory.GetFiles(_directory, "monitors.json.corrupt-*");
            Assert.HasCount(1, corrupt);
            Assert.AreEqual(content, File.ReadAllText(corrupt[0]));
        }

        [TestMethod]
        public void Save_ReadOnlyTarget_ReturnsFalseAndKeepsOldFile()
        {
            MonitorStore store = new(Path);
            store.Save([Definition("a")]);
            var before = File.ReadAllText(Path);
            File.SetAttributes(Path, FileAttributes.ReadOnly);
            Assert.IsFalse(store.Save([Definition("a"), Definition("b")]));
            Assert.AreEqual(before, File.ReadAllText(Path));
            Assert.IsFalse(File.Exists(Path + ".tmp"));
        }

        [TestMethod]
        public void Save_NeverLeavesTempFile()
        {
            new MonitorStore(Path).Save([Definition("a")]);
            CollectionAssert.AreEqual(new[] { Path }, Directory.GetFiles(_directory));
        }

        [TestMethod]
        public void Save_MissingDirectory_Created()
        {
            var nested = System.IO.Path.Combine(_directory, "Config", "monitors.json");
            Assert.IsTrue(new MonitorStore(nested).Save([Definition("a")]));
            Assert.HasCount(1, new MonitorStore(nested).Load());
        }
    }
}
