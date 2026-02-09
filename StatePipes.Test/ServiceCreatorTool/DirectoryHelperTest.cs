using StatePipes.ServiceCreatorTool;

namespace StatePipes.Test.ServiceCreatorTool
{
    [TestClass]
    public class DirectoryHelperTest
    {
        private string _testRootDirectory = null!;

        [TestInitialize]
        public void Setup()
        {
            _testRootDirectory = Path.Combine(Path.GetTempPath(), $"DirectoryHelperTest_{Guid.NewGuid()}");
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
        public void Constructor_CreatesRootDirectoryIfNotExists()
        {
            Assert.IsFalse(Directory.Exists(_testRootDirectory));
            _ = new DirectoryHelper(_testRootDirectory);

            Assert.IsTrue(Directory.Exists(_testRootDirectory));
        }

        [TestMethod]
        public void GetCurrentDirectory_ReturnsRootDirectory_WhenJustCreated()
        {
            var helper = new DirectoryHelper(_testRootDirectory);

            var result = helper.GetCurrentDirectory();

            Assert.AreEqual(_testRootDirectory, result);
        }

        [TestMethod]
        public void GetCurrentDirectoryRelative_ReturnsEmpty_WhenAtRoot()
        {
            var helper = new DirectoryHelper(_testRootDirectory);

            var result = helper.GetCurrentDirectoryRelative();

            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void MoveTo_CreatesSubdirectory()
        {
            var helper = new DirectoryHelper(_testRootDirectory);
            var subdirName = "SubFolder";

            helper.MoveTo(subdirName);

            var expectedPath = Path.Combine(_testRootDirectory, subdirName);
            Assert.IsTrue(Directory.Exists(expectedPath));
        }

        [TestMethod]
        public void MoveTo_UpdatesCurrentDirectory()
        {
            var helper = new DirectoryHelper(_testRootDirectory);
            var subdirName = "SubFolder";

            helper.MoveTo(subdirName);
            var result = helper.GetCurrentDirectory();

            Assert.EndsWith(subdirName, result);
        }

        [TestMethod]
        public void MoveTo_ThrowsException_WhenContainsPathSeparator()
        {
            var helper = new DirectoryHelper(_testRootDirectory);
            var thrown = false;
            try
            {
                helper.MoveTo("Sub\\Folder");
            }
            catch (Exception)
            {
                thrown = true;
            }
            Assert.IsTrue(thrown, "Expected exception was not thrown");
        }

        [TestMethod]
        public void MoveTo_ThrowsException_WhenContainsDriveSeparator()
        {
            var helper = new DirectoryHelper(_testRootDirectory);
            var thrown = false;
            try
            {
                helper.MoveTo("C:");
            }
            catch (Exception)
            {
                thrown = true;
            }
            Assert.IsTrue(thrown, "Expected exception was not thrown");
        }

        [TestMethod]
        public void MoveUp_NavigatesCorrectly_FromSingleLevel()
        {
            var helper = new DirectoryHelper(_testRootDirectory);
            helper.MoveTo("SubFolder");
            var afterMoveTo = helper.GetCurrentDirectory();

            helper.MoveUp();
            var afterMoveUp = helper.GetCurrentDirectory();

            // MoveUp should change the current directory
            Assert.AreNotEqual(afterMoveTo, afterMoveUp);
        }

        [TestMethod]
        public void MoveUp_MovesToParent_FromNestedLevel()
        {
            var helper = new DirectoryHelper(_testRootDirectory);
            helper.MoveTo("Level1");
            helper.MoveTo("Level2");

            helper.MoveUp();
            var result = helper.GetCurrentDirectory();

            Assert.EndsWith("Level1", result);
            Assert.DoesNotContain("Level2", result);
        }

        [TestMethod]
        public void MoveUp_DoesNothing_WhenAtRoot()
        {
            var helper = new DirectoryHelper(_testRootDirectory);
            var before = helper.GetCurrentDirectoryRelative();

            helper.MoveUp();
            var after = helper.GetCurrentDirectoryRelative();

            Assert.AreEqual(before, after);
        }

        [TestMethod]
        public void MoveToRootDirectory_ResetsToRoot()
        {
            var helper = new DirectoryHelper(_testRootDirectory);
            helper.MoveTo("Level1");
            helper.MoveTo("Level2");

            helper.MoveToRootDirectory();
            var result = helper.GetCurrentDirectory();

            Assert.AreEqual(_testRootDirectory, result);
        }

        [TestMethod]
        public void MoveToRootDirectory_SetsRelativePathToEmpty()
        {
            var helper = new DirectoryHelper(_testRootDirectory);
            helper.MoveTo("Level1");

            helper.MoveToRootDirectory();
            var result = helper.GetCurrentDirectoryRelative();

            Assert.AreEqual(string.Empty, result);
        }
    }
}
