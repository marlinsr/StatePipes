using System.Diagnostics;
namespace StatePipes.Diagrammer
{
    internal class GraphGenerator(string graphDirectory)
    {
        private void Create(string pdfFileName, string dotFileName)
        {
            var p = new Process
            {
                StartInfo =
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    FileName = AppDomain.CurrentDomain.BaseDirectory + @"ExternalTools\dot.exe",
                    Arguments = $@"-T pdf -o {pdfFileName} {dotFileName}"
                }
            };
            p.Start();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception("dot.exe exited with code " + p.ExitCode);
            Display(pdfFileName);
        }
        private void Display(string pdfFilename)
        {
            var p = new Process
            {
                StartInfo =
                {
                    UseShellExecute = true,
                    FileName = pdfFilename
                }
            };
            p.Start();
        }
        public void GraphStateMachine(string dotFileName) => Create($@"{graphDirectory}\{Path.GetFileName(Path.ChangeExtension(dotFileName, ""))}pdf", dotFileName);
    }
}
