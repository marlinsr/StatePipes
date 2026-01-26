using Microsoft.VisualStudio.Setup.Configuration;
using System.Diagnostics;

namespace StatePipes.ServiceCreatorTool
{
    internal class VisualStudioLauncher
    {
        public static void LaunchSolution(string solutionFullPath) => LaunchVsDte(arguments: solutionFullPath);
        private static void LaunchVsDte(string arguments)
        {
            ISetupInstance? setupInstance = GetLatest();
            if (setupInstance == null) return;
            string installationPath = setupInstance.GetInstallationPath();
            string executablePath = Path.Combine(installationPath, @"Common7\IDE\devenv.exe");
            Process vsProcess = Process.Start(executablePath, arguments);
        }
        private static ISetupInstance? GetLatest()
        {
            var instances = GetSetupInstances();
            if (instances.Count() <= 0) return null;
            return instances.OrderByDescending(i => new Version(i.GetInstallationVersion())).First();
        }
        private static IEnumerable<ISetupInstance> GetSetupInstances()
        {
            List<ISetupInstance> instances = [];
            ISetupConfiguration setupConfiguration = new SetupConfiguration();
            IEnumSetupInstances enumerator = setupConfiguration.EnumInstances();
            int count;
            do
            {
                ISetupInstance[] setupInstances = new ISetupInstance[1];
                enumerator.Next(1, setupInstances, out count);
                if (count == 1 && setupInstances[0] != null && !IsPreRelease(setupInstances[0]))
                {
                    instances.Add(setupInstances[0]);
                }
            }
            while (count == 1);
            return instances;
        }
        private static bool IsPreRelease(ISetupInstance setupInstance) => ((ISetupInstanceCatalog)setupInstance).IsPrerelease();
    }
}
 