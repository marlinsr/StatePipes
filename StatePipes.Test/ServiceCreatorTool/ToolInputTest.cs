using StatePipes.ServiceCreatorTool;
using static StatePipes.Test.ServiceCreatorTool.ToolTestHelpers;

namespace StatePipes.Test.ServiceCreatorTool
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ToolInputTest
    {
        private static ToolInput WithDialogs(IDialogPrompt dialogs) => new(new Dictionary<string, string>(), false, dialogs);

        [TestMethod]
        public void Supplied_IsUsedWithoutDialog() =>
            Assert.AreEqual("Idle", Headless(("--state", "Idle")).Text("--state", "p"));

        [TestMethod]
        public void Supplied_IsUsedWithoutHeadlessToo() =>
            Assert.AreEqual("Idle", new ToolInput(new Dictionary<string, string> { ["--state"] = "Idle" }, false, new FailingDialogs()).Text("--state", "p"));

        [TestMethod]
        public void HeadlessMissing_PrintsNeedsAndReturnsNull()
        {
            var output = Capture(() => Assert.IsNull(Headless().Text("--state", "p")));

            StringAssert.Contains(output, "Headless mode needs --state.");
        }

        [TestMethod]
        public void HeadlessEmptyValue_CountsAsMissing()
        {
            var output = Capture(() => Assert.IsNull(Headless(("--state", "")).Text("--state", "p")));

            StringAssert.Contains(output, "Headless mode needs --state.");
        }

        [TestMethod]
        public void NotHeadlessMissing_OpensDialog() =>
            Assert.AreEqual("FromDialog", WithDialogs(new StubDialogs(text: "FromDialog")).Text("--state", "p"));

        [TestMethod]
        public void DialogCanceled_PrintsActionCanceled()
        {
            var output = Capture(() => Assert.IsNull(WithDialogs(new StubDialogs(text: null)).Text("--state", "p")));

            StringAssert.Contains(output, "Action canceled.");
        }

        [TestMethod]
        public void Choose_IgnoresCaseAndReturnsOptionSpelling() =>
            Assert.AreEqual("Running", Headless(("--transition-to", "running")).Choose("--transition-to", ["Idle", "Running"], "p"));

        [TestMethod]
        public void Choose_UnknownValue_ListsOptions()
        {
            var output = Capture(() => Assert.IsNull(Headless(("--trigger-scope", "private")).Choose("--trigger-scope", ["public", "internal"], "p")));

            StringAssert.Contains(output, "--trigger-scope private not found; one of: public, internal");
            Assert.IsFalse(output.Contains("build the solution"), output);
        }

        [TestMethod]
        public void Choose_UnknownBuildValue_AddsBuildHint()
        {
            var output = Capture(() => Assert.IsNull(Headless(("--trigger", "Stop")).Choose("--trigger", ["Start", "Poll"], "p", fromBuild: true)));

            StringAssert.Contains(output, "--trigger Stop not found; one of: Start, Poll. If it was just added, build the solution first.");
        }

        [TestMethod]
        public void Choose_NotHeadlessMissing_OpensListDialog() =>
            Assert.AreEqual("Poll", WithDialogs(new StubDialogs(choice: "Poll")).Choose("--trigger", ["Start", "Poll"], "p"));

        [TestMethod]
        public void Confirm_MapsValuesAndRejectsOthers()
        {
            Assert.IsTrue(Headless(("--transport", "RabbitMQ")).Confirm("--transport", "rabbitmq", "kafka", "p"));
            Assert.IsFalse(Headless(("--transport", "kafka")).Confirm("--transport", "rabbitmq", "kafka", "p"));
            var output = Capture(() => Assert.IsNull(Headless(("--transport", "mqtt")).Confirm("--transport", "rabbitmq", "kafka", "p")));
            StringAssert.Contains(output, "--transport must be rabbitmq or kafka.");
        }

        [TestMethod]
        public void Confirm_NotHeadlessMissing_AsksYesNo() =>
            Assert.IsFalse(WithDialogs(new StubDialogs(confirm: false)).Confirm("--transport", "rabbitmq", "kafka", "p"));

        [TestMethod]
        public void Name_AcceptsIdentifiers() =>
            Assert.AreEqual("_Idle2", Headless(("--state", "_Idle2")).Name("--state", "p"));

        [TestMethod]
        public void Name_RejectsNonIdentifiers()
        {
            Assert.IsNull(Headless(("--event", "1st")).Name("--event", "p"));
            var output = Capture(() => Assert.IsNull(Headless(("--state", "Idle State")).Name("--state", "p")));
            StringAssert.Contains(output, "--state must be a C# identifier: Idle State.");
        }

        [TestMethod]
        public void PositiveInt_AcceptsPositiveAndRejectsZeroAndText()
        {
            Assert.AreEqual(500, Headless(("--timer-period", "500")).PositiveInt("--timer-period", "p"));
            Assert.IsNull(Headless(("--timer-period", "0")).PositiveInt("--timer-period", "p"));
            var output = Capture(() => Assert.IsNull(Headless(("--timer-period", "fast")).PositiveInt("--timer-period", "p")));
            StringAssert.Contains(output, "--timer-period must be a positive number of milliseconds.");
        }

        [TestMethod]
        public void File_And_Folder_UseSuppliedPaths()
        {
            Assert.AreEqual(@"C:\a.dll", Headless(("--dll", @"C:\a.dll")).File("--dll", "p", "*.dll"));
            Assert.AreEqual(@"C:\src", Headless(("--repo", @"C:\src")).Folder("--repo", "p"));
        }

        [TestMethod]
        public void WithDefault_UsedOnlyWhenNotSupplied()
        {
            Assert.AreEqual("Valve", Headless().WithDefault("--statemachine", "Valve").Text("--statemachine", "p"));
            Assert.AreEqual("Pump", Headless(("--statemachine", "Pump")).WithDefault("--statemachine", "Valve").Text("--statemachine", "p"));
            Assert.IsTrue(Headless().WithDefault("--statemachine", "Valve").Supplied("--statemachine"));
        }

        [TestMethod]
        public void Supplied_IgnoresArgumentCase() =>
            Assert.IsTrue(Headless(("--State", "Idle")).Supplied("--state"));

        [TestMethod]
        public void ToolResult_OkAndFailed()
        {
            Assert.IsTrue(ToolResult.Ok("f.cs").Succeeded);
            Assert.AreEqual("f.cs", ToolResult.Ok("f.cs").FileToOpen);
            Assert.IsFalse(ToolResult.Failed.Succeeded);
        }
    }
}
