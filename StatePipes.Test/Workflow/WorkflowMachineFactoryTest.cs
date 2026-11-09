using StatePipes.Common.Internal;
using StatePipes.Messages;
using StatePipes.StateMachine.Test.Internal;
using StatePipes.Test.Common;
using StatePipes.Test.Workflow.Samples.Cart;
using StatePipes.Test.Workflow.Samples.Counting;
using StatePipes.Workflow.Internal;
using System.Reflection;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowMachineFactoryTest
    {
        private TestStatePipesService _bus = null!;
        private InMemoryTimerService _timers = null!;
        private readonly WorkflowMetadata _counting = WorkflowMetadata.Build(typeof(CountingWorkflow));

        [TestInitialize]
        public void Init()
        {
            Entries.Count.Clear();
            _bus = new TestStatePipesService();
            _timers = new InMemoryTimerService(_bus, TimeProvider.System);
        }
        [TestCleanup]
        public void Cleanup()
        {
            WorkflowContext.Current = null;
            _timers.Dispose();
            _bus.Dispose();
        }
        private WorkflowContext Ctx(string cid = "c1") => new(null!, cid, _timers);

        [TestMethod]
        public void CreateNew_RunsFirstStateOnEntry_AndLandsInFirstState()
        {
            var wm = WorkflowMachineFactory.CreateNew(_counting, Ctx(), _bus, _timers);
            Assert.AreEqual(nameof(CountFirstState), wm.CurrentState);
            Assert.AreEqual(1, Entries.Of(nameof(CountFirstState)));
            Assert.AreSame(wm.Machine, WorkflowContext.Current!.Machine);
        }

        [TestMethod]
        public void Rehydrate_StartsInPersistedState_DoesNotRunOnEntry()
        {
            var record = new WorkflowRecord(_counting.WorkflowTypeName, "c1", nameof(CountChildBState), "{\"states\":{}}", 3);
            var wm = WorkflowMachineFactory.Rehydrate(_counting, record, Ctx(), _bus, _timers, WorkflowJson.CreateSettings(typeof(CountingWorkflow).Assembly));
            Assert.AreEqual(nameof(CountChildBState), wm.CurrentState);
            Assert.AreEqual(0, Entries.Count.Values.Sum());
        }

        [TestMethod]
        public void Rehydrate_RestoresFields()
        {
            var cart = WorkflowMetadata.Build(typeof(CartWorkflow));
            var json = "{\"states\":{\"CartOpenState\":{\"_lines\":[{\"Sku\":\"a\",\"Qty\":1},{\"Sku\":\"b\",\"Qty\":2}]}}}";
            var record = new WorkflowRecord(cart.WorkflowTypeName, "c1", nameof(CartOpenState), json, 1);
            var wm = WorkflowMachineFactory.Rehydrate(cart, record, Ctx(), _bus, _timers, WorkflowJson.CreateSettings(typeof(CartWorkflow).Assembly));
            var lines = (List<CartLine>)typeof(CartOpenState).GetField("_lines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wm.StatesByName[nameof(CartOpenState)])!;
            Assert.AreEqual(2, lines.Count);
        }

        [TestMethod]
        public void MoveToState_ParentReentry_ReachesFirstSubstate()
        {
            var wm = WorkflowMachineFactory.CreateNew(_counting, Ctx(), _bus, _timers);
            WorkflowMachineFactory.Fire(wm, new CountGo());
            Assert.AreEqual(nameof(CountChildAState), wm.CurrentState);
            WorkflowMachineFactory.Fire(wm, new CountNext());
            Assert.AreEqual(nameof(CountChildBState), wm.CurrentState);
            WorkflowMachineFactory.Fire(wm, new CountReset());
            Assert.AreEqual(nameof(CountChildAState), wm.CurrentState);
        }

        [TestMethod]
        public void WorkflowTransitions_DoNotPublishStateStatusEvent()
        {
            var wm = WorkflowMachineFactory.CreateNew(_counting, Ctx(), _bus, _timers);
            WorkflowMachineFactory.Fire(wm, new CountGo());
            Assert.IsFalse(_bus.EventInvocations.Any(e => e is StateStatusEvent));
        }
    }
}
