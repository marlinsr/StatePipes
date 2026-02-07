using StatePipes.ServiceCreatorTool;

namespace StatePipes.Test.ServiceCreatorTool
{
    [TestClass]
    public class MonikerSubstitutionTest
    {
        [TestMethod]
        public void Replace_WithNoMonikers_ReturnsOriginalString()
        {
            var substitution = new MonikerSubstitution();
            var input = "Hello World";

            var result = substitution.Replace(input);

            Assert.AreEqual("Hello World", result);
        }

        [TestMethod]
        public void Replace_WithSingleMoniker_ReplacesCorrectly()
        {
            var substitution = new MonikerSubstitution();
            substitution.AddMoniker("{{NAME}}", "TestProject");
            var input = "Project: {{NAME}}";

            var result = substitution.Replace(input);

            Assert.AreEqual("Project: TestProject", result);
        }

        [TestMethod]
        public void Replace_WithMultipleMonikers_ReplacesAll()
        {
            var substitution = new MonikerSubstitution();
            substitution.AddMoniker("{{NAME}}", "TestProject");
            substitution.AddMoniker("{{VERSION}}", "1.0.0");
            var input = "{{NAME}} version {{VERSION}}";

            var result = substitution.Replace(input);

            Assert.AreEqual("TestProject version 1.0.0", result);
        }

        [TestMethod]
        public void Replace_WithMultipleOccurrences_ReplacesAllOccurrences()
        {
            var substitution = new MonikerSubstitution();
            substitution.AddMoniker("{{NAME}}", "Test");
            var input = "{{NAME}} and {{NAME}} again";

            var result = substitution.Replace(input);

            Assert.AreEqual("Test and Test again", result);
        }

        [TestMethod]
        public void Replace_WithMonikerNotInString_ReturnsOriginalString()
        {
            var substitution = new MonikerSubstitution();
            substitution.AddMoniker("{{OTHER}}", "Value");
            var input = "No monikers here";

            var result = substitution.Replace(input);

            Assert.AreEqual("No monikers here", result);
        }

        [TestMethod]
        public void AddMoniker_WithSameKey_OverwritesPreviousValue()
        {
            var substitution = new MonikerSubstitution();
            substitution.AddMoniker("{{NAME}}", "First");
            substitution.AddMoniker("{{NAME}}", "Second");
            var input = "{{NAME}}";

            var result = substitution.Replace(input);

            Assert.AreEqual("Second", result);
        }

        [TestMethod]
        public void Replace_WithEmptyString_ReturnsEmptyString()
        {
            var substitution = new MonikerSubstitution();
            substitution.AddMoniker("{{NAME}}", "Test");

            var result = substitution.Replace(string.Empty);

            Assert.AreEqual(string.Empty, result);
        }
    }
}
