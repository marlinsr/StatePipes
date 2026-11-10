using Microsoft.Extensions.Time.Testing;
using Moq;
using Newtonsoft.Json.Linq;
using StatePipes.Common.Internal;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Test.Internal;
using StatePipes.Test.Common;
using StatePipes.Test.Workflow.Samples.Cart;
using StatePipes.Test.Workflow.Samples.Counting;
using StatePipes.Workflow.Internal;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowEngineTest
    {
        private static readonly BusConfig ReplyTo = new("b", "p", "c", "pw");
        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        private TestStatePipesService _bus = null!;
        private InMemoryTimerService _timers = null!;
        private InMemoryWorkflowStore _store = null!;
        private Mock<ICommandRequeuer> _requeuer = null!;
        private RecordingLogger _log = null!;
        private static readonly string Cart = typeof(CartWorkflow).FullName!;

        [TestInitialize]
        public void Init()
        {
            _bus = new TestStatePipesService();
            _timers = new InMemoryTimerService(_bus, _time);
            _store = new InMemoryWorkflowStore(_time);
            _requeuer = new Mock<ICommandRequeuer>();
            _log = new RecordingLogger();
        }
        [TestCleanup]
        public void Cleanup()
        {
            _log.Dispose();
            _timers.Dispose();
            _bus.Dispose();
        }

        private WorkflowEngine Engine(Type workflow, IWorkflowStore? store = null) =>
            new(WorkflowMetadata.Build(workflow), store ?? _store, _bus, _requeuer.Object, _timers,
                WorkflowJson.CreateSettings(workflow.Assembly), new WorkflowEngineOptions());

        private static void Send(WorkflowEngine engine, ITrigger cmd, string cid, int retry = 0, bool timer = false) =>
            engine.Handle(cmd, cid, ReplyTo, retry, timer);

        private int Lines(string cid)
        {
            var doc = JObject.Parse(_store.Instances.Single(i => i.CorrelationId == cid).DocumentJson);
            return ((JArray)doc["states"]!["CartOpenState"]!["_lines"]!).Count;
        }

        [TestMethod]
        public void NewInstance_DeclaredCommand_Persists()
        {
            Send(Engine(typeof(CartWorkflow)), new AddItem { Sku = "a", Qty = 1 }, "c1");
            var rec = _store.Instances.Single();
            Assert.AreEqual((Cart, "c1", nameof(CartOpenState), 1L), (rec.WorkflowType, rec.CorrelationId, rec.CurrentState, rec.Version));
            Assert.AreEqual(1, Lines("c1"));
        }

        [TestMethod]
        public void NewInstance_UndeclaredCommand_NoRow()
        {
            Send(Engine(typeof(CartWorkflow)), new PriceQuoted(), "c1");
            Assert.AreEqual(0, _store.Instances.Count);
            Assert.AreEqual(0, _store.Timers.Count);
        }

        [TestMethod]
        public void PermitReentryIfFalse_StillPersistsFieldChanges()
        {
            var engine = Engine(typeof(CartWorkflow));
            Send(engine, new AddItem { Sku = "a" }, "c1");
            Send(engine, new AddItem { Sku = "b" }, "c1");
            Assert.AreEqual(2, Lines("c1"));
            Assert.AreEqual(2L, _store.Instances.Single().Version);
        }

        [TestMethod]
        public void ExistingInstance_UndeclaredCommand_RollsBack_VersionUnchanged()
        {
            var engine = Engine(typeof(CartWorkflow));
            Send(engine, new AddItem { Sku = "a" }, "c1");
            Send(engine, new PriceQuoted(), "c1");
            Assert.AreEqual(1L, _store.Instances.Single().Version);
        }

        [TestMethod]
        public void Exception_RollsBack_AndCommandDiscarded()
        {
            Send(Engine(typeof(CountingWorkflow)), new CountBoom(), "c1");
            Assert.AreEqual(0, _store.Instances.Count);
            Assert.IsTrue(_log.Contains("boom"));
            _requeuer.VerifyNoOtherCalls();
        }

        [TestMethod]
        public void FinalState_DeletesInstanceAndTimers()
        {
            var engine = Engine(typeof(CartWorkflow));
            Send(engine, new AddItem { Sku = "a" }, "c1");
            Assert.AreEqual(1, _store.Timers.Count);
            Send(engine, new Checkout(), "c1");
            Assert.AreEqual(nameof(PricingState), _store.Instances.Single().CurrentState);
            Send(engine, new PriceQuoted { Total = 3 }, "c1");
            Assert.AreEqual(0, _store.Instances.Count);
            Assert.AreEqual(0, _store.Timers.Count);
        }

        [TestMethod]
        public void Locked_RequeuesAfterDelayWithIncrementedRetry()
        {
            _store.SimulateLock(Cart, "c1");
            var cmd = new AddItem { Sku = "a" };
            Send(Engine(typeof(CartWorkflow)), cmd, "c1");
            _requeuer.VerifyNoOtherCalls();
            _time.Advance(TimeSpan.FromMilliseconds(50));
            InMemoryTimerServiceTest.WaitFor(() => _requeuer.Invocations.Count == 1);
            _requeuer.Verify(r => r.Requeue(cmd, ReplyTo, 1, false), Times.Once);
            Assert.AreEqual(0, _store.Instances.Count);
        }

        [TestMethod]
        public void Locked_AtMaxRetries_LogsAndDiscards()
        {
            _store.SimulateLock(Cart, "c1");
            Send(Engine(typeof(CartWorkflow)), new AddItem(), "c1", retry: 10);
            _time.Advance(TimeSpan.FromSeconds(1));
            Thread.Sleep(100);
            _requeuer.VerifyNoOtherCalls();
            Assert.IsTrue(_log.Contains("Workflow command not satisfied: workflow locked"));
        }

        [TestMethod]
        public void EmptyCorrelationId_DiscardedWithError()
        {
            var store = new Mock<IWorkflowStore>();
            Send(Engine(typeof(CartWorkflow), store.Object), new AddItem(), "");
            store.Verify(s => s.Begin(), Times.Never);
            Assert.IsTrue(_log.At(ILogger.LogLevel.Error).Any(m => m.Contains("CorrelationId")));
        }

        [TestMethod]
        public void TimerDelivery_ForUnknownInstance_DoesNotCreate()
        {
            Send(Engine(typeof(CartWorkflow)), new AddItem { Sku = "a" }, "c9", timer: true);
            Assert.AreEqual(0, _store.Instances.Count);
            Assert.IsTrue(_log.Contains("Workflow timer message for a finished or unknown instance discarded"));
        }

        [TestMethod]
        public void UnknownCurrentState_LogsErrorLeavesRowUntouched()
        {
            var seeded = new WorkflowRecord(Cart, "c1", "RemovedState", "{\"states\":{}}", 5);
            _store.Seed(seeded);
            Send(Engine(typeof(CartWorkflow)), new AddItem(), "c1");
            Assert.AreEqual(seeded, _store.Instances.Single());
            Assert.IsTrue(_log.At(ILogger.LogLevel.Error).Any(m => m.Contains("RemovedState")));
        }

        [TestMethod]
        public void StoreUnavailable_LogsAndDiscards_NoRequeue()
        {
            var store = new Mock<IWorkflowStore>();
            store.Setup(s => s.Begin()).Throws(new InvalidOperationException("database unreachable"));
            Send(Engine(typeof(CartWorkflow), store.Object), new AddItem(), "c1");
            Assert.IsTrue(_log.Contains("database unreachable"));
            _requeuer.VerifyNoOtherCalls();
        }
    }
}
