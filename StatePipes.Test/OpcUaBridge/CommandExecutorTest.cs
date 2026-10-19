using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.Comms;
using StatePipes.OpcUaBridge;
using System.Collections.Concurrent;
using System.Text;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class CommandExecutorTest
    {
        private static readonly BusConfig Client = OpcUaBridgeMonitorTest.Client;
        private static readonly byte[] ClientReplyTo = Encoding.UTF8.GetBytes(OpcUaBridgeMonitorTest.ClientReplyTo);

        /// <summary>
        /// Records every Read, Write and Call in one ordered log. Writes answer with <see cref="Results"/> or Good,
        /// or throw <see cref="Throw"/>; Calls wait for <see cref="CallGate"/> when set, then throw <see cref="CallThrow"/>
        /// or return <see cref="CallResult"/>.
        /// </summary>
        private sealed class FakeSession : IOpcUaSession
        {
            public ConcurrentQueue<string> Log { get; } = new();
            public ConcurrentQueue<IReadOnlyList<NodeId>> Reads { get; } = new();
            public ConcurrentQueue<IReadOnlyList<(NodeId NodeId, Variant Value)>> Writes { get; } = new();
            public ConcurrentQueue<(NodeId ObjectId, NodeId MethodId, IReadOnlyList<Variant> Inputs)> Calls { get; } = new();
            public Func<IReadOnlyList<(NodeId NodeId, Variant Value)>, IReadOnlyList<StatusCode>>? Results { get; set; }
            public Exception? Throw { get; set; }
            public Exception? CallThrow { get; set; }
            public Task? CallGate { get; set; }
            public CallMethodResult CallResult { get; set; } = new() { StatusCode = StatusCodes.Good, OutputArguments = new[] { new Variant(5) } };

            public Task<IReadOnlyList<DataValue>> ReadValuesAsync(IReadOnlyList<NodeId> nodeIds, CancellationToken ct)
            {
                Log.Enqueue("Read");
                Reads.Enqueue(nodeIds);
                return Task.FromResult<IReadOnlyList<DataValue>>([.. nodeIds.Select(_ => new DataValue(new Variant(1.0)))]);
            }

            public Task<IReadOnlyList<StatusCode>> WriteValuesAsync(IReadOnlyList<(NodeId NodeId, Variant Value)> values, CancellationToken ct)
            {
                Log.Enqueue("Write");
                Writes.Enqueue(values);
                var toThrow = Throw;
                Throw = null;
                if (toThrow != null) throw toThrow;
                return Task.FromResult(Results?.Invoke(values) ?? [.. values.Select(_ => (StatusCode)StatusCodes.Good)]);
            }

            public async Task<CallMethodResult> CallAsync(NodeId objectId, NodeId methodId, IReadOnlyList<Variant> inputs, CancellationToken ct)
            {
                Log.Enqueue("Call");
                Calls.Enqueue((objectId, methodId, inputs));
                if (CallGate != null) await CallGate.WaitAsync(ct);
                var toThrow = CallThrow;
                CallThrow = null;
                if (toThrow != null) throw toThrow;
                return CallResult;
            }
        }

        /// <summary>Every answer, in the order sent: Get events, SetStatus and ExecResult.</summary>
        private sealed class FakeOutput : ICommandOutput
        {
            public ConcurrentQueue<(string Kind, OpcUaDataItem? Item, StatusCode Status, JObject? Body)> Sent { get; } = new();

            public void SendGetEvent(OpcUaDataItem item, DataValue value, BusConfig replyTo, byte[] replyToRaw) => Record("Get", item, value.StatusCode, null, replyTo);
            public void SendSetStatus(OpcUaDataItem item, StatusCode statusCode, BusConfig replyTo, byte[] replyToRaw) => Record("SetStatus", item, statusCode, null, replyTo);

            public void SendExecResult(MessageNames names, byte[] body, BusConfig replyTo, byte[] replyToRaw)
            {
                var parsed = JObject.Parse(Encoding.UTF8.GetString(body));
                Record("ExecResult", null, (uint)parsed["StatusCode"]!, parsed, replyTo);
            }

            /// <summary>Lets a test record an answer made outside the executor, e.g. by an EnqueueAlone run.</summary>
            public void Record(string kind, OpcUaDataItem? item, StatusCode status, JObject? body, BusConfig replyTo)
            {
                Assert.AreEqual(Client.CommandExchangeName, replyTo.CommandExchangeName);
                Sent.Enqueue((kind, item, status, body));
            }
        }

        private readonly FakeSession _session = new();
        private readonly FakeOutput _output = new();
        private readonly CancellationTokenSource _cts = new();
        private CommandExecutor _executor = null!;
        private Task _running = Task.CompletedTask;

        [TestInitialize]
        public void Initialize() => _executor = new CommandExecutor(_session, _output);

        [TestCleanup]
        public async Task Cleanup()
        {
            await _cts.CancelAsync();
            await _running;
        }

        private void Start() => _running = _executor.RunAsync(_cts.Token);

        private static OpcUaDataItem Item(string name, Type? valueType = null, BuiltInType builtInType = BuiltInType.Double)
        {
            var nodeId = new NodeId(name, 2);
            return new(nodeId, nodeId.ToString(), OpcUaNaming.ToMessageNames(OpcUaStructureTest.Exchange, nodeId), valueType ?? typeof(double), null, false, builtInType, true);
        }

        private static readonly OpcUaMethodItem Add = CreateAdd();

        private static OpcUaMethodItem CreateAdd()
        {
            NodeId objectId = new("Methods", 2), methodId = new("Methods.Add", 2);
            var names = OpcUaNaming.AssignUniqueMethodNames(OpcUaStructureTest.Exchange, [(objectId, methodId)]).Values.Single();
            return new(objectId, methodId, objectId.ToString(), methodId.ToString(), names,
                [new("a", BuiltInType.Int32, typeof(int), null, false), new("b", BuiltInType.Int32, typeof(int), null, false)],
                [new("sum", BuiltInType.Int32, typeof(int), null, false)]);
        }

        private void Get(OpcUaDataItem item) => _executor.EnqueueGet(item, Client, ClientReplyTo);
        private void Set(OpcUaDataItem item, string body) => _executor.EnqueueSet(item, Encoding.UTF8.GetBytes(body), Client, ClientReplyTo);
        private void Exec(string body = """{"a":2,"b":3}""") => _executor.EnqueueExec(Add, Encoding.UTF8.GetBytes(body), Client, ClientReplyTo);

        private List<(string Kind, OpcUaDataItem? Item, StatusCode Status, JObject? Body)> WaitForAnswers(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_output.Sent.Count < count && DateTime.UtcNow < deadline) Thread.Sleep(10);
            Assert.HasCount(count, _output.Sent);
            return [.. _output.Sent];
        }

        private List<(OpcUaDataItem Item, StatusCode Status)> WaitForStatuses(int count) => [.. WaitForAnswers(count).Select(a => (a.Item!, a.Status))];

        private static double Value((NodeId NodeId, Variant Value) write) => (double)write.Value.AsBoxedObject(Variant.BoxingBehavior.Legacy)!;

        // Sets, as SetWriter served them.

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
        public void ConsecutiveSets_OneWriteUpTo500()
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

        // Ordering across kinds.

        [TestMethod]
        public void GetSetGet_ThreeRunsInOrder()
        {
            var a = Item("a");
            Get(a); Set(a, """{"Value":1}"""); Get(a);
            Start();

            var answers = WaitForAnswers(3);

            CollectionAssert.AreEqual(new[] { "Read", "Write", "Read" }, _session.Log.ToArray());
            CollectionAssert.AreEqual(new[] { "Get", "SetStatus", "Get" }, answers.Select(a => a.Kind).ToArray());
        }

        [TestMethod]
        public void ConsecutiveGets_OneReadUpTo500()
        {
            for (var i = 0; i < 501; i++) Get(Item($"n{i}"));
            Start();

            WaitForAnswers(501);

            CollectionAssert.AreEqual(new[] { 500, 1 }, _session.Reads.Select(r => r.Count).ToArray());
        }

        [TestMethod]
        public void ReadThrows_EveryGetAnsweredWithCode()
        {
            FakeThrowingReadSession session = new();
            _executor = new CommandExecutor(session, _output);
            Get(Item("a")); Get(Item("b"));
            Start();

            var answers = WaitForAnswers(2);

            Assert.IsTrue(answers.All(a => a.Status.Code == StatusCodes.BadTimeout));
        }

        private sealed class FakeThrowingReadSession : IOpcUaSession
        {
            public Task<IReadOnlyList<DataValue>> ReadValuesAsync(IReadOnlyList<NodeId> nodeIds, CancellationToken ct) => throw new ServiceResultException(StatusCodes.BadTimeout);
            public Task<IReadOnlyList<StatusCode>> WriteValuesAsync(IReadOnlyList<(NodeId NodeId, Variant Value)> values, CancellationToken ct) => throw new NotSupportedException();
            public Task<CallMethodResult> CallAsync(NodeId objectId, NodeId methodId, IReadOnlyList<Variant> inputs, CancellationToken ct) => throw new NotSupportedException();
        }

        [TestMethod]
        public void Exec_AlwaysAlone()
        {
            var a = Item("a");
            Exec(); Exec(); Set(a, """{"Value":1}"""); Exec(); Set(Item("b"), """{"Value":1}""");
            Start();

            var answers = WaitForAnswers(5);

            CollectionAssert.AreEqual(new[] { "Call", "Call", "Write", "Call", "Write" }, _session.Log.ToArray());
            CollectionAssert.AreEqual(new[] { "ExecResult", "ExecResult", "SetStatus", "ExecResult", "SetStatus" }, answers.Select(a => a.Kind).ToArray());
        }

        [TestMethod]
        public void Exec_Good_InputsEncodedAndOutputsAnswered()
        {
            Exec();
            Start();

            var answer = WaitForAnswers(1).Single();

            var call = _session.Calls.Single();
            Assert.AreEqual(Add.ObjectId, call.ObjectId);
            Assert.AreEqual(Add.MethodId, call.MethodId);
            CollectionAssert.AreEqual(new object[] { 2, 3 }, call.Inputs.Select(v => v.AsBoxedObject(Variant.BoxingBehavior.Legacy)).ToArray());
            Assert.AreEqual(5, (int)answer.Body!["sum"]!);
        }

        [TestMethod]
        public void Exec_EncodingFails_AnsweredWithoutCall()
        {
            Exec("""{"a":2}""");
            Start();

            Assert.AreEqual(StatusCodes.BadArgumentsMissing, WaitForAnswers(1).Single().Status.Code);
            Assert.IsEmpty(_session.Calls);
        }

        [TestMethod]
        public void Exec_CallThrows_CommunicationErrorThenQueueContinues()
        {
            _session.CallThrow = new InvalidOperationException("boom");
            Exec(); Exec();
            Start();

            var answers = WaitForAnswers(2);

            Assert.AreEqual(StatusCodes.BadCommunicationError, answers[0].Status.Code);
            Assert.AreEqual(StatusCodes.Good, answers[1].Status.Code);
        }

        [TestMethod]
        public void Exec_ServiceResultException_ItsCode()
        {
            _session.CallThrow = new ServiceResultException(StatusCodes.BadSessionClosed);
            Exec();
            Start();

            Assert.AreEqual(StatusCodes.BadSessionClosed, WaitForAnswers(1).Single().Status.Code);
        }

        [TestMethod]
        public async Task Exec_Cancelled_NoAnswer()
        {
            _session.CallGate = new TaskCompletionSource().Task;
            Exec();
            Start();
            WaitUntil(() => !_session.Calls.IsEmpty, "the call");

            await _cts.CancelAsync();
            await _running.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsEmpty(_output.Sent);
        }

        [TestMethod]
        public void SlowExec_HoldsBackLaterCommands()
        {
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _session.CallGate = gate.Task;
            Exec(); Get(Item("a")); Set(Item("b"), """{"Value":1}""");
            Start();
            WaitUntil(() => !_session.Calls.IsEmpty, "the call");
            Thread.Sleep(200);
            CollectionAssert.AreEqual(new[] { "Call" }, _session.Log.ToArray());

            gate.SetResult();

            CollectionAssert.AreEqual(new[] { "ExecResult", "Get", "SetStatus" }, WaitForAnswers(3).Select(a => a.Kind).ToArray());
        }

        [TestMethod]
        public void Alone_NextRunWaitsForTask()
        {
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _executor.EnqueueAlone("monitor", ct => gate.Task.WaitAsync(ct));
            Get(Item("a"));
            Start();
            Thread.Sleep(200);
            Assert.IsEmpty(_session.Log);

            gate.SetResult();

            WaitForAnswers(1);
            CollectionAssert.AreEqual(new[] { "Read" }, _session.Log.ToArray());
        }

        [TestMethod]
        public void Alone_AnswersInQueuePosition()
        {
            var a = Item("a");
            Get(a);
            _executor.EnqueueAlone("refusal", _ => { _output.Record("Refusal", a, StatusCodes.BadNodeIdUnknown, null, Client); return Task.CompletedTask; });
            Get(a);
            Start();

            CollectionAssert.AreEqual(new[] { "Get", "Refusal", "Get" }, WaitForAnswers(3).Select(x => x.Kind).ToArray());
            Assert.HasCount(2, _session.Reads);
        }

        [TestMethod]
        public void Alone_Throws_QueueContinues()
        {
            _executor.EnqueueAlone("broken", _ => throw new InvalidOperationException("boom"));
            Get(Item("a"));
            Start();

            WaitForAnswers(1);
        }

        private static void WaitUntil(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail($"Timed out waiting for {what}");
                Thread.Sleep(10);
            }
        }
    }
}
