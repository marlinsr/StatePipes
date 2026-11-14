using StatePipes.Common;
using StatePipes.Messages;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowDiagramsEventTest
    {
        [TestMethod]
        public void WorkflowDiagramsEvent_RoundTripsThroughJsonUtility()
        {
            var ev = new WorkflowDiagramsEvent("c1", [new WorkflowInstanceDiagram("A.Cart", "PricingState", 3, "digraph{}", "{\"states\":{}}", null),
                                                      new WorkflowInstanceDiagram("A.Count", "", 0, "", "", "boom")]);
            var copy = JsonUtility.GetObjectForJsonString<WorkflowDiagramsEvent>(JsonUtility.GetJsonStringForObject(ev))!;
            Assert.AreEqual("c1", copy.CorrelationId);
            Assert.AreEqual(2, copy.Workflows.Count);
            var w = copy.Workflows[0];
            Assert.AreEqual(("A.Cart", "PricingState", 3L, "digraph{}", "{\"states\":{}}", (string?)null), (w.WorkflowType, w.CurrentState, w.Version, w.DotGraph, w.DocumentJson, w.Error));
            Assert.AreEqual("boom", copy.Workflows[1].Error);
        }

        [TestMethod]
        public void GetWorkflowDiagramsCommand_RoundTripsCorrelationId() =>
            Assert.AreEqual("c1", JsonUtility.Clone(new GetWorkflowDiagramsCommand("c1")).CorrelationId);
    }
}
