using StatePipes.Explorer.NonWebClasses;
using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.Test.Explorer
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowInterrogationSessionTest
    {
        private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static WorkflowDiagramsEvent Reply(string id, int n = 1) =>
            new(id, [.. Enumerable.Range(0, n).Select(i => new WorkflowInstanceDiagram($"A.W{i}", "S", 1, "digraph{}", "{}", null))]);

        [TestMethod]
        public void Tick_ReplyStoredInSameEntryAsSnapshot_Accepted()
        {
            // Explorer's EventJsonRepository keeps one EventEntry per type and replaces its Obj in place.
            var entry = new EventEntry(typeof(WorkflowDiagramsEvent).FullName!, null);
            var session = new WorkflowInterrogationSession();
            session.Start("c1", entry.Obj, T0);
            entry.Obj = Reply("c1", 2);
            Assert.IsTrue(session.Tick(entry.Obj, T0.AddMilliseconds(250)));
            Assert.AreEqual(2, session.Result!.Workflows.Count);
            Assert.AreEqual("2 workflows found", session.Status);
            Assert.IsFalse(session.IsPending);
        }

        [TestMethod]
        public void Tick_PreviousReplyStillPresent_KeepsWaiting()
        {
            IEvent previous = Reply("c1");
            var session = new WorkflowInterrogationSession();
            session.Start("c1", previous, T0);
            Assert.IsFalse(session.Tick(previous, T0.AddSeconds(1)));
            Assert.IsTrue(session.IsPending);
        }

        [TestMethod]
        public void Tick_ReplyForOtherId_KeepsWaiting()
        {
            var session = new WorkflowInterrogationSession();
            session.Start("c1", null, T0);
            Assert.IsFalse(session.Tick(Reply("old"), T0.AddSeconds(1)));
        }

        [TestMethod]
        public void Tick_AfterTenSeconds_TimesOut()
        {
            var session = new WorkflowInterrogationSession();
            session.Start("c1", null, T0);
            Assert.IsFalse(session.Tick(null, T0.AddSeconds(9.9)));
            Assert.IsTrue(session.Tick(null, T0.AddSeconds(10)));
            Assert.AreEqual("No reply after 10 s", session.Status);
            Assert.IsFalse(session.IsPending);
        }

        [TestMethod]
        public void Start_ClearsPreviousResult()
        {
            var session = new WorkflowInterrogationSession();
            session.Start("c1", null, T0);
            session.Tick(Reply("c1"), T0);
            session.Start("c2", null, T0);
            Assert.IsNull(session.Result);
            Assert.AreEqual("Waiting for reply…", session.Status);
            Assert.AreEqual("c2", session.PendingCorrelationId);
        }

        [TestMethod]
        public void Fail_ClearsPendingAndShowsMessage()
        {
            var session = new WorkflowInterrogationSession();
            session.Start("c1", null, T0);
            session.Fail("boom");
            Assert.IsFalse(session.IsPending);
            Assert.AreEqual("boom", session.Status);
        }
    }
}
