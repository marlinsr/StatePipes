using Moq;
using Newtonsoft.Json.Linq;
using StatePipes.Common.Internal;
using StatePipes.Test.Workflow.Samples.Doc;
using StatePipes.Workflow.Internal;
using System.Net;
using System.Reflection;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowDocumentTest
    {
        private readonly WorkflowMetadata _md = WorkflowMetadata.Build(typeof(DocWorkflow));
        private readonly Newtonsoft.Json.JsonSerializerSettings _settings = WorkflowJson.CreateSettings(typeof(DocWorkflow).Assembly);

        [TestCleanup] public void Cleanup() => WorkflowContext.Current = null;

        private Dictionary<string, object> NewStates()
        {
            WorkflowContext.Current = new WorkflowContext(null!, "doc-1", Mock.Of<ITimerService>());
            return _md.StateTypes.ToDictionary(t => t.Name, t => Activator.CreateInstance(t, nonPublic: true)!);
        }
        private static DocFirstState First(Dictionary<string, object> states) => (DocFirstState)states[nameof(DocFirstState)];

        [TestMethod]
        public void RoundTrip_PrivateFieldsAndBackingFields()
        {
            var source = NewStates();
            First(source).Numbers.AddRange([3, 4]);
            First(source).Name = "changed";
            typeof(DocFirstState).GetField("_fixed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(First(source), 7);
            var json = WorkflowDocument.Extract(_md, source, _settings);

            var target = NewStates();
            WorkflowDocument.Populate(_md, target, json, _settings);
            CollectionAssert.AreEqual(new[] { 3, 4 }, First(target).Numbers);
            Assert.AreEqual("changed", First(target).Name);
            Assert.AreEqual(7, First(target).Fixed);
            Assert.AreEqual(5L, First(target).Payload); // boxed primitives in object fields come back as Newtonsoft JSON primitives (long)
        }

        [TestMethod]
        public void Sender_SerializesAsTimerIdOnly()
        {
            var source = NewStates();
            var id = First(source).Timer.TimerId;
            var doc = JObject.Parse(WorkflowDocument.Extract(_md, source, _settings));
            var timer = (JObject)doc["states"]![nameof(DocFirstState)]!["_timer"]!;
            Assert.AreEqual(1, timer.Count, timer.ToString());
            Assert.AreEqual(id, timer["TimerId"]!.ToObject<Guid>());

            var target = NewStates();
            WorkflowDocument.Populate(_md, target, doc.ToString(), _settings);
            Assert.AreEqual(id, First(target).Timer.TimerId);
        }

        [TestMethod]
        public void BaseFields_NotPersisted()
        {
            var json = WorkflowDocument.Extract(_md, NewStates(), _settings);
            Assert.IsFalse(json.Contains("CorrelationId"), json);
            Assert.IsFalse(json.Contains("_workflowContext"), json);
            Assert.IsFalse(json.Contains("Machine"), json);
        }

        [TestMethod]
        public void NewFieldKeepsDefault_RemovedFieldIgnored()
        {
            var json = "{\"states\":{\"DocFirstState\":{\"_gone\":123,\"<Name>k__BackingField\":\"x\"}}}";
            var target = NewStates();
            WorkflowDocument.Populate(_md, target, json, _settings);
            Assert.AreEqual(0, First(target).Numbers.Count);
            Assert.AreEqual("x", First(target).Name);
        }

        [TestMethod]
        public void Binder_RejectsForeignType_ErrorNamesStateAndField()
        {
            var source = NewStates();
            First(source).Payload = IPAddress.Loopback;
            var ex = Assert.ThrowsExactly<WorkflowSerializationException>(() => WorkflowDocument.Extract(_md, source, _settings));
            StringAssert.Contains(ex.Message, "DocFirstState._payload");
        }

        [TestMethod]
        public void Binder_RejectsForeignTypeOnRead()
        {
            var json = "{\"states\":{\"DocFirstState\":{\"_payload\":{\"$type\":\"System.Net.IPAddress, System.Net.Primitives\",\"Address\":1}}}}";
            var ex = Assert.ThrowsExactly<WorkflowSerializationException>(() => WorkflowDocument.Populate(_md, NewStates(), json, _settings));
            StringAssert.Contains(ex.Message, "DocFirstState._payload");
        }

        [TestMethod]
        public void PolymorphicField_TypeAfterOtherKeys_AsJsonbStoresIt()
        {
            // PostgreSQL jsonb orders keys shortest first, so "A" comes back before "$type".
            var json = "{\"states\":{\"DocFirstState\":{\"_payload\":{\"A\":\"x\",\"$type\":\"" + typeof(DocPayload).FullName + ", " + typeof(DocPayload).Assembly.GetName().Name + "\"}}}}";
            var target = NewStates();
            WorkflowDocument.Populate(_md, target, json, _settings);
            Assert.AreEqual(new DocPayload("x"), First(target).Payload);
        }
    }
}
