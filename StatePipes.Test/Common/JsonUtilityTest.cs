using StatePipes.Common;

namespace StatePipes.Test.Common
{
    [TestClass]
    public class JsonUtilityTest
    {
        private class TestObject
        {
            public string? Name { get; set; }
            public int Value { get; set; }
            public List<string>? Items { get; set; }
        }

        [TestMethod]
        public void GetJsonStringForObject_WithSimpleObject_ReturnsValidJson()
        {
            var obj = new TestObject { Name = "Test", Value = 42 };

            var json = JsonUtility.GetJsonStringForObject(obj);

            Assert.IsNotNull(json);
            Assert.Contains("\"Name\":", json);
            Assert.Contains("\"Test\"", json);
            Assert.Contains("\"Value\":", json);
            Assert.Contains("42", json);
        }

        [TestMethod]
        public void GetJsonStringForObject_WithNoFormatting_ReturnsCompactJson()
        {
            var obj = new TestObject { Name = "Test", Value = 42 };

            var json = JsonUtility.GetJsonStringForObject(obj, noFormatting: true);

            Assert.IsNotNull(json);
            Assert.DoesNotContain("\n", json);
        }

        [TestMethod]
        public void GetJsonStringForObject_WithFormatting_ReturnsIndentedJson()
        {
            var obj = new TestObject { Name = "Test", Value = 42 };

            var json = JsonUtility.GetJsonStringForObject(obj, noFormatting: false);

            Assert.IsNotNull(json);
            Assert.Contains("\n", json);
        }

        [TestMethod]
        public void GetJsonStringForObject_WithNull_ReturnsNullString()
        {
            var json = JsonUtility.GetJsonStringForObject(null);

            Assert.AreEqual("null", json);
        }

        [TestMethod]
        public void GetObjectForJsonString_WithValidJson_ReturnsObject()
        {
            var json = "{\"Name\":\"Test\",\"Value\":42}";

            var obj = JsonUtility.GetObjectForJsonString<TestObject>(json);

            Assert.IsNotNull(obj);
            Assert.AreEqual("Test", obj.Name);
            Assert.AreEqual(42, obj.Value);
        }

        [TestMethod]
        public void GetObjectForJsonString_WithListProperty_DeserializesCorrectly()
        {
            var json = "{\"Name\":\"Test\",\"Value\":1,\"Items\":[\"a\",\"b\",\"c\"]}";

            var obj = JsonUtility.GetObjectForJsonString<TestObject>(json);

            Assert.IsNotNull(obj);
            Assert.IsNotNull(obj.Items);
            Assert.HasCount(3, obj.Items);
            Assert.AreEqual("a", obj.Items[0]);
        }

        [TestMethod]
        public void Clone_WithObject_ReturnsDeepCopy()
        {
            var original = new TestObject
            {
                Name = "Original",
                Value = 100,
                Items = ["item1", "item2"]
            };

            var clone = JsonUtility.Clone(original);

            Assert.IsNotNull(clone);
            Assert.AreEqual(original.Name, clone.Name);
            Assert.AreEqual(original.Value, clone.Value);
            Assert.AreNotSame(original, clone);
            Assert.AreNotSame(original.Items, clone.Items);
        }

        [TestMethod]
        public void Clone_ModifyingClone_DoesNotAffectOriginal()
        {
            var original = new TestObject { Name = "Original", Value = 100 };

            var clone = JsonUtility.Clone(original);
            clone.Name = "Modified";
            clone.Value = 200;

            Assert.AreEqual("Original", original.Name);
            Assert.AreEqual(100, original.Value);
        }

        [TestMethod]
        public void GetObjectFromJson_WithType_ReturnsCorrectType()
        {
            var json = "{\"Name\":\"Dynamic\",\"Value\":999}";

            var obj = JsonUtility.GetObjectFromJson(json, typeof(TestObject));

            Assert.IsNotNull(obj);
            Assert.IsInstanceOfType<TestObject>(obj);
            Assert.AreEqual("Dynamic", (obj as TestObject)?.Name ?? string.Empty);
        }

        [TestMethod]
        public void RoundTrip_SerializeAndDeserialize_PreservesData()
        {
            var original = new TestObject
            {
                Name = "RoundTrip",
                Value = 12345,
                Items = ["one", "two", "three"]
            };

            var json = JsonUtility.GetJsonStringForObject(original);
            var restored = JsonUtility.GetObjectForJsonString<TestObject>(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual(original.Name, restored.Name);
            Assert.AreEqual(original.Value, restored.Value);
            Assert.AreEqual(original.Items?.Count, restored.Items?.Count);
        }
    }
}
