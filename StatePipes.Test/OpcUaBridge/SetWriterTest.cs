using Opc.Ua;
using StatePipes.Comms;
using StatePipes.OpcUaBridge;
using System.Collections.Concurrent;
using System.Text;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class SetWriterTest
    {
        private static readonly BusConfig Client = OpcUaBridgeMonitorTest.Client;
        private static readonly byte[] ClientReplyTo = Encoding.UTF8.GetBytes(OpcUaBridgeMonitorTest.ClientReplyTo);

        /// <summary>Records each Write; answers with <see cref="Results"/> or Good, or throws <see cref="Throw"/>.</summary>
        private sealed class FakeSetSession : ISetSession
        {
            public ConcurrentQueue<IReadOnlyList<(NodeId NodeId, Variant Value)>> Writes { get; } = new();
            public Func<IReadOnlyList<(NodeId NodeId, Variant Value)>, IReadOnlyList<StatusCode>>? Results { get; set; }
            public Exception? Throw { get; set; }

            public Task<IReadOnlyList<StatusCode>> WriteValuesAsync(IReadOnlyList<(NodeId NodeId, Variant Value)> values, CancellationToken ct)
            {
                Writes.Enqueue(values);
                var toThrow = Throw;
                Throw = null;
                if (toThrow != null) throw toThrow;
                return Task.FromResult(Results?.Invoke(values) ?? [.. values.Select(_ => (StatusCode)StatusCodes.Good)]);
            }
        }

        private sealed class FakeSetOutput : ISetOutput
        {
            public ConcurrentQueue<(OpcUaDataItem Item, StatusCode Status)> Sent { get; } = new();

            public void SendSetStatus(OpcUaDataItem item, StatusCode statusCode, BusConfig replyTo, byte[] replyToRaw)
            {
                Assert.AreEqual(Client.CommandExchangeName, replyTo.CommandExchangeName);
                Sent.Enqueue((item, statusCode));
            }
        }

        private readonly FakeSetSession _session = new();
        private readonly FakeSetOutput _output = new();
        private readonly CancellationTokenSource _cts = new();
        private SetWriter _writer = null!;
        private Task _running = Task.CompletedTask;

        [TestInitialize]
        public void Initialize() => _writer = new SetWriter(_session, _output);

        [TestCleanup]
        public async Task Cleanup()
        {
            await _cts.CancelAsync();
            await _running;
        }

        private void Start() => _running = _writer.RunAsync(_cts.Token);

        private static OpcUaDataItem Item(string name, Type? valueType = null, BuiltInType builtInType = BuiltInType.Double)
        {
            var nodeId = new NodeId(name, 2);
            return new(nodeId, nodeId.ToString(), OpcUaNaming.ToMessageNames(OpcUaStructureTest.Exchange, nodeId), valueType ?? typeof(double), null, false, builtInType, true);
        }

        private void Set(OpcUaDataItem item, string body) => _writer.Enqueue(item, Encoding.UTF8.GetBytes(body), Client, ClientReplyTo);

        private List<(OpcUaDataItem Item, StatusCode Status)> WaitForStatuses(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_output.Sent.Count < count && DateTime.UtcNow < deadline) Thread.Sleep(10);
            Assert.HasCount(count, _output.Sent);
            return [.. _output.Sent];
        }

        private static double Value((NodeId NodeId, Variant Value) write) => (double)write.Value.AsBoxedObject(Variant.BoxingBehavior.Legacy)!;

        [TestMethod]
        public void Sets_QueuedTogether_OneWrite()
        {
            OpcUaDataItem a = Item("a"), b = Item("b"), c = Item("c");
            Set(a, """{"Value":1}"""); Set(b, """{"Value":2}"""); Set(c, """{"Value":3}""");
            Start();

            var statuses = WaitForStatuses(3);

            Assert.HasCount(1, _session.Writes);
            CollectionAssert.AreEqual(new[] { a.NodeId, b.NodeId, c.NodeId }, _session.Writes.Single().Select(w => w.NodeId).ToArray());
            CollectionAssert.AreEqual(new[] { a, b, c }, statuses.Select(s => s.Item).ToArray());
            Assert.IsTrue(statuses.All(s => s.Status.Code == StatusCodes.Good));
        }

        [TestMethod]
        public void Batch_RepeatedNode_SplitsAndPreservesOrder()
        {
            OpcUaDataItem a = Item("a"), b = Item("b");
            Set(a, """{"Value":1}"""); Set(b, """{"Value":1}"""); Set(a, """{"Value":2}"""); Set(a, """{"Value":3}""");
            Start();

            var statuses = WaitForStatuses(4);

            var writes = _session.Writes.ToList();
            Assert.HasCount(3, writes);
            CollectionAssert.AreEqual(new[] { a.NodeId, b.NodeId }, writes[0].Select(w => w.NodeId).ToArray());
            Assert.AreEqual(2.0, Value(writes[1].Single()));
            Assert.AreEqual(3.0, Value(writes[2].Single()));
            CollectionAssert.AreEqual(new[] { a, b, a, a }, statuses.Select(s => s.Item).ToArray());
        }

        [TestMethod]
        public void Batch_Over500_Split()
        {
            for (var i = 0; i < 501; i++) Set(Item($"n{i}"), """{"Value":1}""");
            Start();

            WaitForStatuses(501);

            CollectionAssert.AreEqual(new[] { 500, 1 }, _session.Writes.Select(w => w.Count).ToArray());
        }

        [TestMethod]
        public void EncodingFailure_AnsweredWithoutWrite()
        {
            var a = Item("a");
            Set(a, """{"Value":"x"}"""); Set(a, "{}"); Set(a, "not json"); Set(a, """{"Value":null}""");
            Start();

            var statuses = WaitForStatuses(4);

            Assert.IsTrue(statuses.All(s => s.Status.Code == StatusCodes.BadTypeMismatch));
            Assert.IsEmpty(_session.Writes);
        }

        [TestMethod]
        public void EncodingFailureInBatch_OthersStillWritten()
        {
            OpcUaDataItem bad = Item("bad"), good = Item("good");
            Set(bad, """{"Value":"x"}"""); Set(good, """{"Value":4.5}""");
            Start();

            var statuses = WaitForStatuses(2);

            Assert.AreEqual(good.NodeId, _session.Writes.Single().Single().NodeId);
            Assert.AreEqual(StatusCodes.BadTypeMismatch, statuses.Single(s => s.Item == bad).Status.Code);
            Assert.AreEqual(StatusCodes.Good, statuses.Single(s => s.Item == good).Status.Code);
        }

        [TestMethod]
        public void NotConnected_PassedThrough()
        {
            _session.Results = values => [.. values.Select(_ => (StatusCode)StatusCodes.BadNotConnected)];
            Set(Item("a"), """{"Value":1}""");
            Start();

            Assert.AreEqual(StatusCodes.BadNotConnected, WaitForStatuses(1)[0].Status.Code);
        }

        [TestMethod]
        public void WriteThrows_EverySetInBatchGetsCode()
        {
            _session.Throw = new ServiceResultException(StatusCodes.BadTimeout);
            Set(Item("a"), """{"Value":1}"""); Set(Item("b"), """{"Value":1}""");
            Start();
            var timedOut = WaitForStatuses(2);
            Assert.IsTrue(timedOut.All(s => s.Status.Code == StatusCodes.BadTimeout));

            _session.Throw = new InvalidOperationException("boom");
            Set(Item("c"), """{"Value":1}""");
            Assert.AreEqual(StatusCodes.BadCommunicationError, WaitForStatuses(3)[2].Status.Code);

            Set(Item("d"), """{"Value":1}""");
            Assert.AreEqual(StatusCodes.Good, WaitForStatuses(4)[3].Status.Code, "The next batch still runs");
        }

        [TestMethod]
        public void PerNodeResults_ReachTheRightSet()
        {
            _session.Results = values => [.. values.Select(v => (StatusCode)(v.NodeId == new NodeId("b", 2) ? StatusCodes.BadUserAccessDenied : StatusCodes.Good))];
            OpcUaDataItem a = Item("a"), b = Item("b");
            Set(a, """{"Value":1}"""); Set(b, """{"Value":1}""");
            Start();

            var statuses = WaitForStatuses(2);

            Assert.AreEqual(StatusCodes.Good, statuses.Single(s => s.Item == a).Status.Code);
            Assert.AreEqual(StatusCodes.BadUserAccessDenied, statuses.Single(s => s.Item == b).Status.Code);
        }

        [TestMethod]
        public void IntegerItem_WrittenWithItsExactType()
        {
            var item = Item("i", typeof(short), BuiltInType.Int16);
            Set(item, """{"Value":-7}""");
            Start();

            WaitForStatuses(1);

            Assert.AreEqual(BuiltInType.Int16, _session.Writes.Single().Single().Value.TypeInfo.BuiltInType);
        }
    }
}
