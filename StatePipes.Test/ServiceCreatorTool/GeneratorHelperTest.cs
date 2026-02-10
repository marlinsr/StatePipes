using StatePipes.ServiceCreatorTool;

namespace StatePipes.Test.ServiceCreatorTool
{
    [TestClass]
    public class GeneratorHelperTest
    {
        private string _testRootDirectory = null!;

        [TestInitialize]
        public void Setup()
        {
            _testRootDirectory = Path.Combine(Path.GetTempPath(), $"GeneratorHelperTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testRootDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testRootDirectory))
            {
                Directory.Delete(_testRootDirectory, recursive: true);
            }
        }

        [TestMethod]
        public void GetTopLevelAssembly_ReturnsNonNullAssembly()
        {
            var assembly = GeneratorHelper.GetTopLevelAssembly();

            Assert.IsNotNull(assembly);
        }

        [TestMethod]
        public void GetTopLevelAssembly_ReturnsAssemblyWithName()
        {
            var assembly = GeneratorHelper.GetTopLevelAssembly();

            Assert.IsNotNull(assembly.FullName);
            Assert.IsGreaterThan(0, assembly.FullName.Length);
        }

        [TestMethod]
        public void MyPath_ReturnsNonEmptyPath()
        {
            var path = GeneratorHelper.MyPath();

            Assert.IsNotNull(path);
            Assert.IsGreaterThan(0, path.Length);
        }

        [TestMethod]
        public void MyPath_ReturnsExistingDirectory()
        {
            var path = GeneratorHelper.MyPath();

            Assert.IsTrue(Directory.Exists(path));
        }

        [TestMethod]
        public void FileName_ReturnsNonEmptyString()
        {
            var fileName = GeneratorHelper.FileName();

            Assert.IsNotNull(fileName);
            Assert.IsGreaterThan(0, fileName.Length);
        }

        [TestMethod]
        public void MoveToRootDirectory_DelegatesToDirectoryHelper()
        {
            var dm = new DirectoryHelper(_testRootDirectory);
            var monikers = new MonikerSubstitution();
            var helper = new GeneratorHelper(dm, monikers);

            dm.MoveTo("SubFolder");
            helper.MoveToRootDirectory();

            Assert.AreEqual(_testRootDirectory, dm.GetCurrentDirectory());
        }

        [TestMethod]
        public void MoveUp_DelegatesToDirectoryHelper()
        {
            var dm = new DirectoryHelper(_testRootDirectory);
            var monikers = new MonikerSubstitution();
            var helper = new GeneratorHelper(dm, monikers);

            dm.MoveTo("Level1");
            dm.MoveTo("Level2");
            helper.MoveUp();

            Assert.EndsWith("Level1", dm.GetCurrentDirectory());
        }

        [TestMethod]
        public void MoveTo_AppliesMonikerSubstitution()
        {
            var dm = new DirectoryHelper(_testRootDirectory);
            var monikers = new MonikerSubstitution();
            monikers.AddMoniker("{{PROJECT}}", "MyProject");
            var helper = new GeneratorHelper(dm, monikers);

            helper.MoveTo("{{PROJECT}}");

            Assert.EndsWith("MyProject", dm.GetCurrentDirectory());
        }

        [TestMethod]
        public void MoveTo_CreatesDirectory()
        {
            var dm = new DirectoryHelper(_testRootDirectory);
            var monikers = new MonikerSubstitution();
            var helper = new GeneratorHelper(dm, monikers);

            helper.MoveTo("NewFolder");

            var expectedPath = Path.Combine(_testRootDirectory, "NewFolder");
            Assert.IsTrue(Directory.Exists(expectedPath));
        }

        [TestMethod]
        public void FileOperation_CanWriteAndReadFile()
        {
            var testFilePath = Path.Combine(_testRootDirectory, "test.txt");
            var content = "Before\n<!-- INJECTION_POINT -->\nAfter";

            File.WriteAllText(testFilePath, content);
            var readContent = File.ReadAllText(testFilePath);

            Assert.Contains("<!-- INJECTION_POINT -->", readContent);
        }
    }
}
