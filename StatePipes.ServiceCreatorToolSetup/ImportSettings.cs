using EnvDTE80;
using System.Reflection;

namespace StatePipes.ServiceCreatorToolSetup
{
    internal class ImportSettings
    {
        public static void ImportSettingsFromResource(DTE2 dte, string resourceFileName)
        {
            string tempFileName = Path.GetTempFileName();
            string tempVsSettingsFileName = Path.ChangeExtension(tempFileName, ".vssettings");
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using Stream? stream = assembly.GetManifestResourceStream(resourceFileName);
                if (stream == null) throw new FileNotFoundException($"Embedded resource '{resourceFileName}' not found.");
                using FileStream fileStream = new(tempVsSettingsFileName, FileMode.Create, FileAccess.Write);
                stream.CopyTo(fileStream);
                fileStream.Close();
                stream.Close();
                dte.ExecuteCommand($"Tools.ImportandExportSettings /import:\"{tempVsSettingsFileName}\"");
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (File.Exists(tempVsSettingsFileName)) File.Delete(tempVsSettingsFileName);
            }
        }
    }
}
