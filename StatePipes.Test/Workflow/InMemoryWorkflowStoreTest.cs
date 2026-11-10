using Microsoft.Extensions.Time.Testing;
using StatePipes.Workflow.Internal;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class InMemoryWorkflowStoreTest
    {
        private const string Wf = "Samples.CartWorkflow";
        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        private InMemoryWorkflowStore NewStore() => new(_time);
        private static TimerRecord Timer(string cid, TimeSpan? period = null, string wf = Wf) =>
            new(Guid.NewGuid(), wf, cid, period, "Some.Message", "{}");

        [TestMethod]
        public void Rollback_DiscardsStagedWrites()
        {
            var store = NewStore();
            using (var uow = store.Begin())
            {
                uow.Upsert(Wf, "c1", "Open", "{}");
                uow.UpsertTimer(Timer("c1"), TimeSpan.FromSeconds(1));
                Assert.IsNotNull(uow.Load(Wf, "c1"));
            }
            Assert.AreEqual(0, store.Instances.Count);
            Assert.AreEqual(0, store.Timers.Count);
        }

        [TestMethod]
        public void Commit_AppliesAndIncrementsVersion()
        {
            var store = NewStore();
            for (int i = 0; i < 2; i++)
            {
                using var uow = store.Begin();
                uow.Upsert(Wf, "c1", $"S{i}", "{}");
                uow.Commit();
            }
            var rec = store.Instances.Single();
            Assert.AreEqual(2, rec.Version);
            Assert.AreEqual("S1", rec.CurrentState);
        }

        [TestMethod]
        public void TryLock_FailsWhileOtherUnitHoldsIt_AndAfterSimulateLock()
        {
            var store = NewStore();
            using (var a = store.Begin())
            {
                Assert.IsTrue(a.TryLock(Wf, "c1"));
                using var b = store.Begin();
                Assert.IsFalse(b.TryLock(Wf, "c1"));
                Assert.IsTrue(b.TryLock(Wf, "c2"));
            }
            using (var c = store.Begin()) Assert.IsTrue(c.TryLock(Wf, "c1"));
            store.SimulateLock(Wf, "c1");
            using (var d = store.Begin()) Assert.IsFalse(d.TryLock(Wf, "c1"));
            store.ReleaseSimulatedLock(Wf, "c1");
            using (var e = store.Begin()) Assert.IsTrue(e.TryLock(Wf, "c1"));
        }

        [TestMethod]
        public void FireDueTimers()
        {
            var store = NewStore();
            var oneShot = Timer("c1");
            var periodic = Timer("c2", TimeSpan.FromMinutes(1));
            var stuck = Timer("c3");
            var otherType = Timer("c4", wf: "Other.Workflow");
            using (var uow = store.Begin())
            {
                uow.UpsertTimer(oneShot, TimeSpan.Zero);
                uow.UpsertTimer(periodic, TimeSpan.Zero);
                uow.UpsertTimer(stuck, TimeSpan.Zero);
                uow.UpsertTimer(otherType, TimeSpan.Zero);
                uow.Commit();
            }
            _time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(15));
            List<Guid> sent = [];
            var fired = store.FireDueTimers([Wf], 100, t => { if (t.TimerId == stuck.TimerId) return false; sent.Add(t.TimerId); return true; });

            Assert.AreEqual(2, fired);
            CollectionAssert.AreEquivalent(new[] { oneShot.TimerId, periodic.TimerId }, sent);
            Assert.IsFalse(store.Timers.Any(t => t.TimerId == oneShot.TimerId));
            Assert.IsTrue(store.Timers.Any(t => t.TimerId == stuck.TimerId));
            Assert.IsTrue(store.Timers.Any(t => t.TimerId == otherType.TimerId));
            Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 10, 11, 0, TimeSpan.Zero), store.ExpirationOf(periodic.TimerId));
        }

        [TestMethod]
        public void DeleteInstanceAndTimers_RemovesOnlyThatInstancesTimers()
        {
            var store = NewStore();
            var keep = Timer("c2");
            using (var uow = store.Begin())
            {
                uow.Upsert(Wf, "c1", "Open", "{}");
                uow.Upsert(Wf, "c2", "Open", "{}");
                uow.UpsertTimer(Timer("c1"), TimeSpan.FromDays(1));
                uow.UpsertTimer(keep, TimeSpan.FromDays(1));
                uow.Commit();
            }
            using (var uow = store.Begin())
            {
                uow.DeleteInstanceAndTimers(Wf, "c1");
                uow.Commit();
            }
            Assert.AreEqual("c2", store.Instances.Single().CorrelationId);
            Assert.AreEqual(keep.TimerId, store.Timers.Single().TimerId);
        }

        [TestMethod]
        public void TimerExists_SeesStagedWrites()
        {
            var store = NewStore();
            var t = Timer("c1");
            using var uow = store.Begin();
            uow.UpsertTimer(t, TimeSpan.FromSeconds(5));
            Assert.IsTrue(uow.TimerExists(t.TimerId));
            uow.DeleteTimer(t.TimerId);
            Assert.IsFalse(uow.TimerExists(t.TimerId));
        }
    }
}
