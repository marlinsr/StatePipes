using System.Reflection;

namespace StatePipes.ProcessLevelServices
{
    public class DirHelper
    {
        public static void InitializeDirs(string? postFix, string? companyNameOverride)
        {
            _programDirPostfix = postFix ?? string.Empty;
            _companyNameOverride = companyNameOverride ?? string.Empty;
        }
        private static string _programDirPostfix = "";
        private static string _companyNameOverride = "";
        public enum FileCategory
        {
            Config,
            Log,
            Certs
        }
        public static string Find(string filename, FileCategory fileCategory)
        {
            string fileNameOnly = Path.GetFileName(filename);

            if (fileNameOnly != null)
            {
                // Start with the ProgramData directory
                var fullPath = Path.Combine(GetProductDataCategoryDirectoryForProcess(fileCategory), fileNameOnly);
                if (File.Exists(fullPath)) return fullPath;

                // Then go to the application\category subdirectory directory
                fullPath = Path.Combine(Path.Combine(GetApplicationBaseDirectory(), fileCategory.ToString()), fileNameOnly);
                if (File.Exists(fullPath)) return fullPath;

                // Finally go to the application directory
                fullPath = Path.Combine(GetApplicationBaseDirectory(), fileNameOnly);
                if (File.Exists(fullPath)) return fullPath;
            }

            return string.Empty;
        }
        private static string GetApplicationBaseDirectory() => AppDomain.CurrentDomain.SetupInformation.ApplicationBase ?? string.Empty;
        public static string GetProductDataCategoryDirectoryForProcess(FileCategory fileCategory) => Path.Combine(GetProductDataDirectoryForProcess(), fileCategory.ToString());
        public static string GetProcessName()
        {
            var currentProcess = Assembly.GetEntryAssembly()?.GetName()?.Name;
            if(currentProcess == null) currentProcess = Assembly.GetCallingAssembly().GetName().Name;
            if (currentProcess == null) return string.Empty;
            return currentProcess;
        }
        private static string GetProductDataDirectoryForProcess() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), GetCompanyName(), GetProcessName() + _programDirPostfix);
        private static string GetCompanyName()
        {
            if(!string.IsNullOrEmpty(_companyNameOverride)) return _companyNameOverride;
            var assembly = Assembly.GetEntryAssembly();
            var attributes = assembly?.GetCustomAttributes(typeof(AssemblyCompanyAttribute), false);
            if (attributes == null || attributes.Length == 0) return "StatePipes";
            var company = ((AssemblyCompanyAttribute)attributes[0]).Company;
            if(company == assembly?.GetName().Name) return string.Empty;
            return company;
        }
    }
}
