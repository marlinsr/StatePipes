using StatePipes.ServiceCreatorTool;
using static StatePipes.Test.ServiceCreatorTool.ToolTestHelpers;

namespace StatePipes.Test.ServiceCreatorTool
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ToolArgumentsTest
    {
        [TestMethod]
        public void Parse_CollectsPairsAndSkipsFlags() =>
            CollectionAssert.AreEquivalent(new Dictionary<string, string> { ["--state"] = "Idle", ["--parent-state"] = "Operating" },
                ToolArguments.Parse(["-a", "-headless", "--state", "Idle", "-r", "x", "--parent-state", "Operating"])!);

        [TestMethod]
        public void Parse_LooksUpNamesIgnoringCase() =>
            Assert.AreEqual("Idle", ToolArguments.Parse(["--STATE", "Idle"])!["--state"]);

        [TestMethod]
        public void Parse_UnknownArgument_Fails()
        {
            var output = Capture(() => Assert.IsNull(ToolArguments.Parse(["--colour", "red"])));

            StringAssert.Contains(output, "Unknown argument --colour.");
        }

        [TestMethod]
        public void Parse_MissingValueAtEnd_Fails()
        {
            var output = Capture(() => Assert.IsNull(ToolArguments.Parse(["-a", "--state"])));

            StringAssert.Contains(output, "--state needs a value.");
        }

        [TestMethod]
        public void Parse_FlagInsteadOfValue_Fails()
        {
            var output = Capture(() => Assert.IsNull(ToolArguments.Parse(["--state", "-headless"])));

            StringAssert.Contains(output, "--state needs a value.");
        }

        [TestMethod]
        public void Known_HasEveryHeadlessArgument()
        {
            string[] expected = ["--repo", "--transport", "--solution-name", "--prefix", "--statemachine-name", "--statemachine", "--state",
                "--parent-state", "--trigger-name", "--trigger-scope", "--event", "--trigger", "--transition-to", "--timer-name",
                "--timer-period", "--moniker", "--dll", "--broker", "--exchange", "--cert", "--certpw", "--timeout"];

            CollectionAssert.AreEquivalent(expected, ToolArguments.Known.ToArray());
        }
    }
}
