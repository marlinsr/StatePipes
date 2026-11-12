using Microsoft.Extensions.Time.Testing;
using Moq;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Test.Internal;
using StatePipes.Test.Workflow.Samples.Cart;
using StatePipes.Test.Workflow.Samples.Counting;
using StatePipes.Workflow.Internal;
using System.Text.RegularExpressions;

namespace StatePipes.Test.Workflow
{
    /// <summary>Shared engine fixture for the interrogation tests (copied from <see cref="WorkflowEngineTest"/>).</summary>
    public abstract class WorkflowInterrogateTestBase
    {
        protected static readonly BusConfig ReplyTo = new("b", "p", "c", "pw");
        protected static readonly string Cart = typeof(CartWorkflow).FullName!;
        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        internal TestStatePipesService _bus = null!;
        internal InMemoryTimerService _timers = null!;
        internal InMemoryWorkflowStore _store = null!;
        internal Mock<ICommandRequeuer> _requeuer = null!;

        [TestInitialize]
        public void Init()
        {
            _bus = new TestStatePipesService();
            _timers = new InMemoryTimerService(_bus, _time);
            _store = new InMemoryWorkflowStore(_time);
            _requeuer = new Mock<ICommandRequeuer>();
        }
        [TestCleanup]
        public void Cleanup()
        {
            _timers.Dispose();
            _bus.Dispose();
        }

        internal WorkflowEngine Engine(Type workflow, IWorkflowStore? store = null) =>
            new(WorkflowMetadata.Build(workflow), store ?? _store, _bus, _requeuer.Object, _timers,
                WorkflowJson.CreateSettings(workflow.Assembly), new WorkflowEngineOptions());

        internal static void Send(WorkflowEngine engine, ITrigger cmd, string cid) => engine.Handle(cmd, cid, ReplyTo, 0, false);
    }

    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowInterrogateTest : WorkflowInterrogateTestBase
    {
        [TestMethod]
        public void Interrogate_UnknownId_ReturnsNull() => Assert.IsNull(Engine(typeof(CartWorkflow)).Interrogate("nope"));

        [TestMethod]
        public void Interrogate_RunningInstance_ReturnsStateVersionDocumentAndHighlightedDiagram()
        {
            var e = Engine(typeof(CartWorkflow));
            Send(e, new AddItem { Sku = "a", Qty = 1 }, "c1");
            Send(e, new Checkout(), "c1");
            var rec = _store.Instances.Single();
            var d = e.Interrogate("c1")!;
            Assert.AreEqual((Cart, nameof(PricingState), rec.Version, rec.DocumentJson, (string?)null), (d.WorkflowType, d.CurrentState, d.Version, d.DocumentJson, d.Error));
            Assert.IsTrue(Regex.IsMatch(d.DotGraph, $@"{nameof(PricingState)}[^\n]*style\s*=\s*""?filled"), d.DotGraph);
            Assert.IsFalse(Regex.IsMatch(d.DotGraph, $@"{nameof(CartOpenState)}[^\n]*style\s*=\s*""?filled"), d.DotGraph);
        }

        [TestMethod]
        public void Interrogate_DoesNotMutateAndLaterCommandsStillWork()
        {
            var e = Engine(typeof(CartWorkflow));
            Send(e, new AddItem { Sku = "a" }, "c1");
            var before = _store.Instances.Single().Version;
            e.Interrogate("c1");
            Assert.AreEqual(before, _store.Instances.Single().Version);
            Send(e, new AddItem { Sku = "b" }, "c1");
            Assert.AreEqual(before + 1, _store.Instances.Single().Version);
        }

        [TestMethod]
        public void Interrogate_DoesNotRunOnEntryOrSendCommands()
        {
            var e = Engine(typeof(CountingWorkflow));
            Send(e, new CountGo(), "c1");
            var parent = Entries.Of(nameof(CountParentState));
            var child = Entries.Of(nameof(CountChildAState));
            Assert.AreEqual(nameof(CountChildAState), e.Interrogate("c1")!.CurrentState);
            Assert.AreEqual((parent, child), (Entries.Of(nameof(CountParentState)), Entries.Of(nameof(CountChildAState))));
        }

        [TestMethod]
        public void Interrogate_WhileLocked_StillReturnsInstance()
        {
            var e = Engine(typeof(CartWorkflow));
            Send(e, new AddItem { Sku = "a" }, "c1");
            using var holder = _store.Begin();
            Assert.IsTrue(holder.TryLock(Cart, "c1"));
            Assert.AreEqual(nameof(CartOpenState), e.Interrogate("c1")!.CurrentState);
        }

        [TestMethod]
        public void Interrogate_UnknownPersistedState_ReturnsDiagramAndError()
        {
            using (var uow = _store.Begin()) { uow.Upsert(Cart, "c9", "GoneState", "{\"states\":{}}"); uow.Commit(); }
            var d = Engine(typeof(CartWorkflow)).Interrogate("c9")!;
            Assert.AreEqual("GoneState", d.CurrentState);
            StringAssert.Contains(d.Error, "Unknown state GoneState");
            StringAssert.Contains(d.DotGraph, nameof(CartOpenState));
        }

        [TestMethod]
        public void Interrogate_StoreThrows_ReturnsErrorEntry()
        {
            var bad = new Mock<IWorkflowStore>();
            bad.Setup(s => s.Begin()).Throws(new InvalidOperationException("db down"));
            var d = Engine(typeof(CartWorkflow), bad.Object).Interrogate("c1")!;
            Assert.AreEqual((Cart, "", 0L, "", "db down"), (d.WorkflowType, d.CurrentState, d.Version, d.DotGraph, d.Error));
        }
    }
}
