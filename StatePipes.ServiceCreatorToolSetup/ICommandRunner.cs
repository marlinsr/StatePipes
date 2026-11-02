using System.Diagnostics;

namespace StatePipes.ServiceCreatorToolSetup
{
    internal sealed record CommandResult(int ExitCode, string Output);
    internal interface ICommandRunner
    {
        CommandResult Run(string fileName, IReadOnlyList<string> arguments);
    }
    /// <summary>Runs a program and captures its output; a program still running after two minutes is killed and reports exit code -1.</summary>
    internal sealed class ProcessCommandRunner : ICommandRunner
    {
        private static readonly TimeSpan timeout = TimeSpan.FromSeconds(120);
        public CommandResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Couldn't start {fileName}");
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                return new CommandResult(-1, $"timed out after {timeout.TotalSeconds} seconds");
            }
            return new CommandResult(process.ExitCode, stdout.Result + stderr.Result);
        }
    }
}
