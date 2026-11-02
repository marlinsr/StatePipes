using System.Text.Json;

namespace StatePipes.Test.ClaudePlugin
{
    /// <summary>The Installer ships the plugin as a local marketplace and registers it; these check its NSIS script.</summary>
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class InstallerPluginTest
    {
        private static string InstallerDir => Path.Combine(PluginPaths.RepoRoot(), "StatePipes.ServiceCreatorTool.Installer");
        private static string Nsi => File.ReadAllText(Path.Combine(InstallerDir, "StatePipes.ServiceCreatorTool.Installer.nsi"));
        private static string Between(string text, string start, string end)
        {
            var from = text.IndexOf(start, StringComparison.Ordinal);
            Assert.IsTrue(from >= 0, $"{start} not found");
            var to = text.IndexOf(end, from, StringComparison.Ordinal);
            return text[from..to];
        }
        private static string InstallSection => Between(Nsi, "Section \"MainSection\"", "SectionEnd");
        private static string UninstallSection => Between(Nsi, "Section Uninstall", "SectionEnd");
        private static string[] Lines(string section) => section.Split('\n').Select(l => l.Trim()).ToArray();

        [TestMethod]
        public void Installer_MarketplaceNamesThePlugin()
        {
            using var marketplace = JsonDocument.Parse(File.ReadAllText(Path.Combine(InstallerDir, "ClaudePlugin", ".claude-plugin", "marketplace.json")));
            using var plugin = JsonDocument.Parse(File.ReadAllText(Path.Combine(PluginPaths.RepoRoot(), "StatePipes.ClaudePlugin", ".claude-plugin", "plugin.json")));
            var plugins = marketplace.RootElement.GetProperty("plugins").EnumerateArray().ToList();

            Assert.AreEqual("statepipes", marketplace.RootElement.GetProperty("name").GetString());
            Assert.HasCount(1, plugins);
            Assert.AreEqual(plugin.RootElement.GetProperty("name").GetString(), plugins[0].GetProperty("name").GetString());
            Assert.AreEqual("./statepipes", plugins[0].GetProperty("source").GetString());
        }

        [TestMethod]
        public void Installer_CopiesEveryPluginEntryButTests()
        {
            var pluginRoot = Path.Combine(PluginPaths.RepoRoot(), "StatePipes.ClaudePlugin");
            var section = InstallSection;
            var missing = new List<string>();
            foreach (var entry in Directory.GetFileSystemEntries(pluginRoot).Select(Path.GetFileName).Where(e => e != "tests"))
            {
                var expected = entry switch
                {
                    ".claude-plugin" => @"File ""..\StatePipes.ClaudePlugin\.claude-plugin\plugin.json""",
                    _ when Directory.Exists(Path.Combine(pluginRoot, entry!)) => $@"File /r ""..\StatePipes.ClaudePlugin\{entry}""",
                    _ => $@"File ""..\StatePipes.ClaudePlugin\{entry}""",
                };
                if (!section.Contains(expected)) missing.Add(expected);
            }

            Assert.IsEmpty(missing, string.Join(Environment.NewLine, missing));
            Assert.IsFalse(Nsi.Contains(@"StatePipes.ClaudePlugin\tests"));
        }

        [TestMethod]
        public void Installer_ClearsOldPluginBeforeCopying()
        {
            var lines = Lines(InstallSection);
            var clear = Array.IndexOf(lines, @"RMDir /r ""$INSTDIR\ClaudePlugin""");
            var firstCopy = Array.FindIndex(lines, l => l.StartsWith("File") && l.Contains("StatePipes.ClaudePlugin"));

            Assert.IsTrue(clear >= 0 && firstCopy > clear, $"clear {clear}, first copy {firstCopy}");
        }

        [TestMethod]
        public void Installer_RegistersAfterCopyingThePlugin()
        {
            var lines = Lines(InstallSection);
            var lastCopy = Array.FindLastIndex(lines, l => l.StartsWith("File") && l.Contains("ClaudePlugin"));
            var setup = Array.FindIndex(lines, l => l.StartsWith("ExecWait") && l.Contains("StatePipes.ServiceCreatorToolSetup.exe"));

            Assert.IsTrue(lastCopy >= 0 && setup > lastCopy, $"last copy {lastCopy}, setup {setup}");
        }

        [TestMethod]
        public void Uninstaller_RemovesPluginBeforeDeletingSetup()
        {
            var lines = Lines(UninstallSection);
            var remove = Array.FindIndex(lines, l => l.StartsWith("ExecWait") && l.Contains("--remove-plugin"));
            var deleteSetup = Array.FindIndex(lines, l => l.StartsWith(@"Delete ""$INSTDIR\ServiceCreatorToolSetup\"));

            Assert.IsTrue(remove >= 0 && deleteSetup > remove, $"remove {remove}, delete setup {deleteSetup}");
            CollectionAssert.Contains(lines, @"RMDir /r ""$INSTDIR\ClaudePlugin""");
        }
    }
}
