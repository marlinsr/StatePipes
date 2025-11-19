using Microsoft.VisualStudio.Setup.Configuration;
using System.Xml.Linq;

namespace StatePipes.ServiceCreatorToolSetup
{
    internal class SetupEnvironment
    {
        private const string statePipesLocalNugetsEnvironmentVariableName = "StatePipesLocalNugets";
        private const string statePipesPrivateNugets = "StatePipes Private Nugets";
        private static void SetupVSSettings(System.Diagnostics.Process vsProcess)
        {
            System.Threading.Thread.Sleep(10000);
            var dte = ExternalDTE.GetDTE2(vsProcess.Id);
            if (dte == null) throw new Exception($"Couldn't find process {vsProcess.ProcessName} id {vsProcess.Id}");
            string resourceFileName = $"{typeof(ImportSettings).Namespace}.Resources.StatePipesExternalToolsSettings.vssettings";
            ImportSettings.ImportSettingsFromResource(dte, resourceFileName);
        }
        private static bool IsPreRelease(ISetupInstance setupInstance)
        {
            ISetupInstanceCatalog setupInstanceCatalog = (ISetupInstanceCatalog)setupInstance;
            return setupInstanceCatalog.IsPrerelease();
        }
        private static void SetupToolsForInstances(IEnumSetupInstances enumerator, string tempTextFileName)
        {
            System.Diagnostics.Process? vsProcess = null;
            int count = 1;
            while (count == 1)
            {
                ISetupInstance[] setupInstances = new ISetupInstance[1];
                enumerator.Next(1, setupInstances, out count);
                if (count != 1 || setupInstances[0] == null || IsPreRelease(setupInstances[0])) continue;
                string installationPath = setupInstances[0].GetInstallationPath();
                string executablePath = Path.Combine(installationPath, @"Common7\IDE\devenv.exe");
                vsProcess = System.Diagnostics.Process.Start(executablePath, $"{tempTextFileName} /nosplash");
                System.Threading.Thread.Sleep(10000);
                SetupVSSettings(vsProcess);
                vsProcess?.Kill();
            }
        }
        private static void SetupTools()
        {
            string tempFileName = Path.GetTempFileName();
            string tempTextFileName = Path.ChangeExtension(tempFileName, ".txt");
            File.WriteAllText(tempTextFileName, "Do Not Touch. This instance of visual studio will close automatically when done adding external tools.");
            try
            {
                ISetupConfiguration setupConfiguration = new SetupConfiguration();
                IEnumSetupInstances enumerator = setupConfiguration.EnumInstances();
                SetupToolsForInstances(enumerator, tempTextFileName);
            }
            catch { throw; }
            finally { if (File.Exists(tempTextFileName)) File.Delete(tempTextFileName); }
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
