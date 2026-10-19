using Opc.Ua;
using StatePipes.OpcUaBridge;
using System.Text;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ExecArgumentEncoderTest
    {
        private static readonly MethodArgument[] AddInputs =
        [
            new("a", BuiltInType.Int32, typeof(int), null, false),
            new("b", BuiltInType.Int32, typeof(int), null, false)
        ];

        private static StatusCode Encode(string body, IReadOnlyList<MethodArgument> inputs, out IReadOnlyList<Variant> values) =>
            ExecArgumentEncoder.TryEncode(Encoding.UTF8.GetBytes(body), inputs, out values);

        [TestMethod]
        public void Encode_AddInputs_TypedVariantsInInputOrder()
        {
            var status = Encode("""{"b":3,"a":2}""", AddInputs, out var values);

            Assert.AreEqual(StatusCodes.Good, status.Code);
            CollectionAssert.AreEqual(new object[] { 2, 3 }, values.Select(v => v.AsBoxedObject(Variant.BoxingBehavior.Legacy)).ToArray());
            Assert.IsTrue(values.All(v => v.TypeInfo.BuiltInType == BuiltInType.Int32));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("  ")]
        [DataRow("{}")]
        public void Encode_EmptyBodyNoInputs_Good(string body)
        {
            Assert.AreEqual(StatusCodes.Good, Encode(body, [], out var values).Code);
            Assert.IsEmpty(values);
        }

        [TestMethod]
        public void Encode_Missing_BadArgumentsMissing() => Assert.AreEqual(StatusCodes.BadArgumentsMissing, Encode("""{"a":2}""", AddInputs, out _).Code);

        [TestMethod]
        [DataRow("[1]")]
        [DataRow("5")]
        [DataRow("x")]
        [DataRow("")]
        public void Encode_NotObject_BadArgumentsMissing(string body) => Assert.AreEqual(StatusCodes.BadArgumentsMissing, Encode(body, AddInputs, out _).Code);

        [TestMethod]
        public void Encode_Extra_BadTooManyArguments() => Assert.AreEqual(StatusCodes.BadTooManyArguments, Encode("""{"a":2,"b":3,"c":4}""", AddInputs, out _).Code);

        [TestMethod]
        public void Encode_MissingAndExtra_MissingWins() => Assert.AreEqual(StatusCodes.BadArgumentsMissing, Encode("""{"a":2,"c":4}""", AddInputs, out _).Code);

        [TestMethod]
        public void Encode_WrongKind_BadTypeMismatch() => Assert.AreEqual(StatusCodes.BadTypeMismatch, Encode("""{"a":"2","b":3}""", AddInputs, out _).Code);

        [TestMethod]
        public void Encode_OutOfRange_BadOutOfRange() => Assert.AreEqual(StatusCodes.BadOutOfRange, Encode("""{"a":3000000000,"b":0}""", AddInputs, out _).Code);

        [TestMethod]
        public async Task Encode_Structure_ExtensionObject()
        {
            var layout = (await OpcUaStructureTest.CreateBuilder().BuildAsync(OpcUaStructureTest.DensityTypeId, CancellationToken.None))!;
            MethodArgument[] inputs = [new("density", BuiltInType.ExtensionObject, typeof(string), layout, false)];

            var status = Encode("""{"density":{"Volume":1.5,"Mass":3}}""", inputs, out var values);

            Assert.AreEqual(StatusCodes.Good, status.Code);
            Assert.AreEqual(BuiltInType.ExtensionObject, values[0].TypeInfo.BuiltInType);
        }
    }
}
