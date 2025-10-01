using System.Diagnostics;

namespace StatePipes.ServiceCreatorTool
{
    internal class PdfOpener
    {
        public static void OpenPdfFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
        }
    }
}
