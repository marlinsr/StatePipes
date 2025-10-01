using System.Xml.Linq;

namespace StatePipes.ServiceCreatorToolSetup
{
    internal class SetupEnvironment
    {
        private const string statePipesLocalNugetsEnvironmentVariableName = "StatePipesLocalNugets";
        private const string statePipesPrivateNugets = "StatePipes Private Nugets";

        private static void SetupTools()
        {
            string tempFileName = Path.GetTempFileName();
            string tempTextFileName = Path.ChangeExtension(tempFileName, ".txt");
            File.WriteAllText(tempTextFileName, "Do Not Touch. This instance of visual studio will close automatically when done adding external tools.");
            var vsProcess = VisualStudioLauncher.LaunchSolution(tempTextFileName);
            Thread.Sleep(10000);
            try
            {
                var dte = ExternalDTE.GetDTE2(vsProcess.Id);
                if (dte == null) throw new Exception($"Couldn't find process {vsProcess.ProcessName} id {vsProcess.Id}");
                string resourceFileName = $"{typeof(ImportSettings).Namespace}.Resources.StatePipesExternalToolsSettings.vssettings";
                ImportSettings.ImportSettingsFromResource(dte, resourceFileName);
            }
            catch { throw; }
            finally
            {
                vsProcess.Kill();
                if (File.Exists(tempTextFileName)) File.Delete(tempTextFileName);
            }
        }
        private static void SetupNugets()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(statePipesLocalNugetsEnvironmentVariableName)))
            {
                var localRepoDirectory = $@"{Environment.GetEnvironmentVariable("USERPROFILE")}\.statepipes\PrivateNugets";
                Directory.CreateDirectory(localRepoDirectory);
                Environment.SetEnvironmentVariable(statePipesLocalNugetsEnvironmentVariableName, localRepoDirectory, EnvironmentVariableTarget.User);
            }
            var nugetConfigFileName = $@"{Environment.GetEnvironmentVariable("APPDATA")}\NuGet\NuGet.Config";
            var nugetConfig = XDocument.Load(nugetConfigFileName);
            var packageSourcesElement = nugetConfig.Descendants("packageSources").FirstOrDefault();
            if (packageSourcesElement == null) return;
            var source = packageSourcesElement.Elements("add").FirstOrDefault(source =>
            source.Attribute("key")?.Value == statePipesPrivateNugets);
            if (source != null) return;
            XElement newSource = new("add");
            newSource.Add(new XAttribute("key", statePipesPrivateNugets));
            newSource.Add(new XAttribute("value", $"%{statePipesLocalNugetsEnvironmentVariableName}%"));
            packageSourcesElement.Add(newSource);
            nugetConfig.Save(nugetConfigFileName);
        }
        public static void Setup()
        {
            SetupTools();
            SetupNugets();
        }
    }
}
