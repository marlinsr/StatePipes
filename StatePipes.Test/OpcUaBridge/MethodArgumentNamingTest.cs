using StatePipes.OpcUaBridge;

namespace StatePipes.Test.OpcUaBridge
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class MethodArgumentNamingTest
    {
        [TestMethod]
        public void PropertyNames_SanitizedAndUnique()
        {
            var names = MethodArgumentNaming.PropertyNames(["a", "a", "", "1x", "b.c", null], new HashSet<string>());
            CollectionAssert.AreEqual(new[] { "a", "a_2", "Arg2", "_1x", "b_c", "Arg5" }, names.ToArray());
        }

        [TestMethod]
        public void PropertyNames_OutputReserved_Suffixed()
        {
            CollectionAssert.AreEqual(new[] { "Status_2", "sum" }, MethodArgumentNaming.PropertyNames(["Status", "sum"], MethodArgumentNaming.OutputReserved).ToArray());
            CollectionAssert.AreEqual(new[] { "Status_2", "Status_2_2" }, MethodArgumentNaming.PropertyNames(["Status", "Status_2"], MethodArgumentNaming.OutputReserved).ToArray());
            CollectionAssert.AreEqual(new[] { "ObjectId_2", "MethodId_2", "StatusCode_2" },
                MethodArgumentNaming.PropertyNames(["ObjectId", "MethodId", "StatusCode"], MethodArgumentNaming.OutputReserved).ToArray());
        }
    }
}
