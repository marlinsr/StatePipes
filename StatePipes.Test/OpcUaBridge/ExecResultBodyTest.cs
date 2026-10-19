using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.OpcUaBridge;
using System.Text;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ExecResultBodyTest
    {
        private static readonly OpcUaMethodItem Add = Method("Add",
            [new("a", BuiltInType.Int32, typeof(int), null, false), new("b", BuiltInType.Int32, typeof(int), null, false)],
            [new("sum", BuiltInType.Int32, typeof(int), null, false)]);

        private static readonly OpcUaMethodItem Reject = Method("Reject", [new("percent", BuiltInType.Double, typeof(double), null, false)], []);

        private static OpcUaMethodItem Method(string name, MethodArgument[] inputs, MethodArgument[] outputs)
        {
            NodeId objectId = new("Methods", 2), methodId = new($"Methods.{name}", 2);
            var names = OpcUaNaming.AssignUniqueMethodNames("Line1Plc", [(objectId, methodId)]).Values.Single();
            return new(objectId, methodId, objectId.ToString(), methodId.ToString(), names, inputs, outputs);
        }

        private static JObject Parse(byte[] body) => JObject.Parse(Encoding.UTF8.GetString(body));

        [TestMethod]
        public void Create_Good_OutputsAndIds()
        {
            var body = Parse(ExecResultBody.Create(Add, new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = new[] { new Variant(5) } }));

            Assert.IsTrue(JToken.DeepEquals(JObject.Parse("""{"ObjectId":"ns=2;s=Methods","MethodId":"ns=2;s=Methods.Add","StatusCode":0,"Status":"Good","sum":5}"""), body), body.ToString());
        }

        [TestMethod]
        public void Create_Bad_NoOutputs()
        {
            var body = Parse(ExecResultBody.Create(Add, new CallMethodResult { StatusCode = StatusCodes.BadInternalError, OutputArguments = new[] { new Variant(5) } }));

            Assert.AreEqual(StatusCodes.BadInternalError.Code, (uint)body["StatusCode"]!);
            Assert.IsNull(body["sum"]);
        }

        [TestMethod]
        public void Create_Uncertain_OutputsIncluded()
        {
            var body = Parse(ExecResultBody.Create(Add, new CallMethodResult { StatusCode = StatusCodes.Uncertain, OutputArguments = new[] { new Variant(5) } }));

            Assert.AreEqual(5, (int)body["sum"]!);
        }

        [TestMethod]
        public void Create_NullOutput_Omitted()
        {
            var missing = Parse(ExecResultBody.Create(Add, new CallMethodResult { StatusCode = StatusCodes.Good }));
            var nullValue = Parse(ExecResultBody.Create(Add, new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = new[] { Variant.Null } }));

            Assert.IsNull(missing["sum"]);
            Assert.IsNull(nullValue["sum"]);
        }

        [TestMethod]
        public void Create_InvalidArgument_NamesRejectedInputs()
        {
            CallMethodResult result = new()
            {
                StatusCode = StatusCodes.BadInvalidArgument,
                InputArgumentResults = new[] { (StatusCode)StatusCodes.BadOutOfRange }
            };

            var body = Parse(ExecResultBody.Create(Reject, result));

            Assert.AreEqual($"{result.StatusCode}: percent ({result.InputArgumentResults[0]})", (string)body["Status"]!);
        }

        [TestMethod]
        public void Refused_NullMethod_EmptyIds()
        {
            var body = Parse(ExecResultBody.Refused(null, StatusCodes.BadMethodInvalid));

            Assert.AreEqual(string.Empty, (string)body["ObjectId"]!);
            Assert.AreEqual(string.Empty, (string)body["MethodId"]!);
            Assert.AreEqual(StatusCodes.BadMethodInvalid.Code, (uint)body["StatusCode"]!);
        }

        [TestMethod]
        public void Refused_Method_IdsNoOutputs()
        {
            var body = Parse(ExecResultBody.Refused(Add, StatusCodes.BadTypeMismatch));

            Assert.AreEqual("ns=2;s=Methods.Add", (string)body["MethodId"]!);
            Assert.IsNull(body["sum"]);
        }
    }
}
