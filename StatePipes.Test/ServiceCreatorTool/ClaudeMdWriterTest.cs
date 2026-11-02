using StatePipes.ServiceCreatorTool;

namespace StatePipes.Test.ServiceCreatorTool
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ClaudeMdWriterTest
    {
        private const string PumpRow = "| Pump | Acme.Widgets.Pump | Acme.Widgets.Pump/StatePipesModel.md |";
        private string _dir = string.Empty;
        private string ClaudeMd => Path.Combine(_dir, ClaudeMdWriter.FileName);

        [TestInitialize]
        public void Init()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"ClaudeMdWriterTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [TestMethod]
        public void Render_SubstitutesSolutionAndVersionOnly()
        {
            var text = ClaudeMdWriter.Render("Packages.Acme.Widgets", "6.1.0");

            Assert.IsTrue(text.StartsWith("# Packages.Acme.Widgets Project Memory"), text);
            StringAssert.Contains(text, "(StatePipes 6.1.0)");
            StringAssert.Contains(text, "dotnet build Packages.Acme.Widgets.slnx");
            StringAssert.Contains(text, "<Prefix>");
            Assert.IsFalse(text.Contains("<Solution>"), text);
            Assert.IsFalse(text.Contains("<version>"), text);
        }

        [TestMethod]
        public void WriteNew_DoesNotOverwriteAnExistingFile()
        {
            File.WriteAllText(ClaudeMd, "keep");

            ClaudeMdWriter.WriteNew(_dir, "Packages.Acme.Widgets", "6.1.0");

            Assert.AreEqual("keep", File.ReadAllText(ClaudeMd));
        }

        [TestMethod]
        public void AddServiceRow_InsertsAboveTheMarker()
        {
            ClaudeMdWriter.WriteNew(_dir, "Packages.Acme.Widgets", "6.1.0");

            Assert.IsTrue(ClaudeMdWriter.AddServiceRow(_dir, "Acme.Widgets.Pump"));

            var lines = File.ReadAllLines(ClaudeMd);
            var marker = Array.FindIndex(lines, l => l.StartsWith(ClaudeMdWriter.RowMarker));
            Assert.AreEqual(PumpRow, lines[marker - 1]);
        }

        [TestMethod]
        public void AddServiceRow_SecondTimeAddsNothing()
        {
            ClaudeMdWriter.WriteNew(_dir, "Packages.Acme.Widgets", "6.1.0");
            ClaudeMdWriter.AddServiceRow(_dir, "Acme.Widgets.Pump");

            Assert.IsFalse(ClaudeMdWriter.AddServiceRow(_dir, "Acme.Widgets.Pump"));

            Assert.AreEqual(1, File.ReadAllLines(ClaudeMd).Count(l => l == PumpRow));
        }

        [TestMethod]
        public void AddServiceRow_NoFileOrNoMarker_WritesNothing()
        {
            Assert.IsFalse(ClaudeMdWriter.AddServiceRow(_dir, "Acme.Widgets.Pump"));
            Assert.IsFalse(File.Exists(ClaudeMd));

            File.WriteAllText(ClaudeMd, "# Notes\n| Service | Class library | Model |\n");
            Assert.IsFalse(ClaudeMdWriter.AddServiceRow(_dir, "Acme.Widgets.Pump"));
            Assert.AreEqual("# Notes\n| Service | Class library | Model |\n", File.ReadAllText(ClaudeMd));
        }

        [TestMethod]
        public void AddServiceRow_KeepsCrlfLineEndings()
        {
            File.WriteAllText(ClaudeMd, "## Services\r\n\r\n| Service | Class library | Model |\r\n|---|---|---|\r\n" +
                $"{ClaudeMdWriter.RowMarker}. New StatePipes Service adds rows above this line. -->\r\n");

            Assert.IsTrue(ClaudeMdWriter.AddServiceRow(_dir, "Acme.Widgets.Pump"));

            var text = File.ReadAllText(ClaudeMd);
            StringAssert.Contains(text, PumpRow + "\r\n");
            Assert.IsFalse(text.Replace("\r\n", "").Contains('\n'), text);
        }
    }
}
