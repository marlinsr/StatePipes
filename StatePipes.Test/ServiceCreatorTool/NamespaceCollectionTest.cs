using Newtonsoft.Json;
using Opc.Ua;
using StatePipes.Common;
using StatePipes.Messages;
using StatePipes.OpcUaBridge;
using StatePipes.ServiceCreatorTool;

namespace StatePipes.Test.ServiceCreatorTool
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class NamespaceCollectionTest
    {
        [TestMethod]
        public void AddNameSpaceFromTypeFullName_LiveOpcUaBridgeTypes_OnlyReferencedNamespacesAdded()
        {
            NodeId nodeId = new("Line1.Temperature", 2);
            OpcUaDataItem temperature = new(nodeId, nodeId.ToString(), OpcUaNaming.ToMessageNames("Line1Plc", nodeId), typeof(double));
            var json = JsonUtility.GetJsonStringForObject(new SelfDescriptionEvent(SelfDescriptionBuilder.Build([temperature])), true);
            var typeSerializationList = JsonConvert.DeserializeObject<SelfDescriptionEventEnvelope>(json)!.TypeList!;
            LiveTypeSource source = new(typeSerializationList);
            NamespaceCollection namespaces = [];

            var typeFullNames = typeSerializationList.TypeSerializations.SelectMany(ts => ts.TypeRepo.Keys).Distinct().ToList();
            CollectionAssert.Contains(typeFullNames, "StatePipes.OpcUaBridge.Messages.MonitorState");
            foreach (var typeFullName in typeFullNames) namespaces.AddNameSpaceFromTypeFullName(typeFullName, source);

            CollectionAssert.DoesNotContain(namespaces, "StatePipes.OpcUaBridge.Messages");
            CollectionAssert.Contains(namespaces, "System");
        }
    }
}
