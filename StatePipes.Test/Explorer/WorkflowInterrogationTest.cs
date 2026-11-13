using StatePipes.Explorer.NonWebClasses;
using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.Test.Explorer
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowInterrogationTest
    {
        private static IEvent Reply(string id) => new WorkflowDiagramsEvent(id, []);

        [TestMethod] public void NormalizeInput_Trims() => Assert.AreEqual("abc", WorkflowInterrogation.NormalizeInput("  abc \t"));
        [TestMethod] public void NormalizeInput_WhitespaceIsNull() => Assert.IsNull(WorkflowInterrogation.NormalizeInput("   "));
        [TestMethod] public void NormalizeInput_NullIsNull() => Assert.IsNull(WorkflowInterrogation.NormalizeInput(null));
        [TestMethod] public void IsMatchingReply_NewEntrySameId_True() => Assert.IsTrue(WorkflowInterrogation.IsMatchingReply(Reply("c1"), null, "c1"));
        [TestMethod] public void IsMatchingReply_SameInstanceAsSnapshot_False() { var e = Reply("c1"); Assert.IsFalse(WorkflowInterrogation.IsMatchingReply(e, e, "c1")); }
        [TestMethod]
        public void IsMatchingReply_EntryUpdatedInPlace_True()
        {
            var entry = new EventEntry(typeof(WorkflowDiagramsEvent).FullName!, null);
            var snapshot = entry.Obj;
            entry.Obj = new WorkflowDiagramsEvent("c1", []);
            Assert.IsTrue(WorkflowInterrogation.IsMatchingReply(entry.Obj, snapshot, "c1"));
        }
        [TestMethod] public void IsMatchingReply_OtherId_False() => Assert.IsFalse(WorkflowInterrogation.IsMatchingReply(Reply("old"), null, "c1"));
        [TestMethod] public void IsMatchingReply_Null_False() => Assert.IsFalse(WorkflowInterrogation.IsMatchingReply(null, null, "c1"));
        [TestMethod] public void PrettyJson_Indents() => StringAssert.Contains(WorkflowInterrogation.PrettyJson("{\"a\":1}"), "\n");
        [TestMethod] public void PrettyJson_InvalidReturnsInput() => Assert.AreEqual("{not json", WorkflowInterrogation.PrettyJson("{not json"));
        [TestMethod] public void ShortTypeName_AfterLastDot() => Assert.AreEqual("CartWorkflow", WorkflowInterrogation.ShortTypeName("A.B.CartWorkflow"));
        [TestMethod] public void ShortTypeName_NoDot() => Assert.AreEqual("Cart", WorkflowInterrogation.ShortTypeName("Cart"));
        [TestMethod]
        public void StatusText_Variants()
        {
            Assert.AreEqual("No running workflows for 'c1'", WorkflowInterrogation.StatusText("c1", 0));
            Assert.AreEqual("1 workflow found", WorkflowInterrogation.StatusText("c1", 1));
            Assert.AreEqual("2 workflows found", WorkflowInterrogation.StatusText("c1", 2));
        }
    }
}
