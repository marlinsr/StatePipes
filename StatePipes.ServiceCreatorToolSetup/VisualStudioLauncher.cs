using Microsoft.VisualStudio.Setup.Configuration;
using System.Diagnostics;

namespace StatePipes.ServiceCreatorToolSetup
{
    internal class VisualStudioLauncher
    {
        public static Process LaunchSolution(string solutionFullPath) => LaunchVsDte(isPreRelease: false, arguments: $"{solutionFullPath} /nosplash");
        private static Process LaunchVsDte(bool isPreRelease, string arguments)
        {
            ISetupInstance setupInstance = GetSetupInstance(isPreRelease);
            string installationPath = setupInstance.GetInstallationPath();
            string executablePath = Path.Combine(installationPath, @"Common7\IDE\devenv.exe");
            return Process.Start(executablePath, arguments);
        }
        private static ISetupInstance GetSetupInstance(bool isPreRelease) => GetSetupInstances().First(i => IsPreRelease(i) == isPreRelease);
        private static IEnumerable<ISetupInstance> GetSetupInstances()
        {
            ISetupConfiguration setupConfiguration = new SetupConfiguration();
            IEnumSetupInstances enumerator = setupConfiguration.EnumInstances();

            int count;
            do
            {
                ISetupInstance[] setupInstances = new ISetupInstance[1];
                enumerator.Next(1, setupInstances, out count);
                if (count == 1 && setupInstances[0] != null)
                {
                    yield return setupInstances[0];
                }
            }
            while (count == 1);
        }
        private static bool IsPreRelease(ISetupInstance setupInstance)
        {
            ISetupInstanceCatalog setupInstanceCatalog = (ISetupInstanceCatalog)setupInstance;
            return setupInstanceCatalog.IsPrerelease();
        }
    }
}
