using System.Diagnostics;

namespace StatePipes.ServiceCreatorToolSetup
{
    /// <summary>Output is stdout only, so JSON is parsed without stderr warnings; Error is stderr.</summary>
    internal sealed record CommandResult(int ExitCode, string Output, string Error = "");
    internal interface ICommandRunner
    {
        CommandResult Run(string fileName, IReadOnlyList<string> arguments);
    }
    /// <summary>
    /// Runs a program and captures its output. A program still running after two minutes is killed and reports exit code -1,
    /// and so does one whose output is still held open by a child it left running, so the Installer never waits forever.
    /// </summary>
    internal sealed class ProcessCommandRunner(TimeSpan? timeout = null) : ICommandRunner
    {
        private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(120);
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
            var deadline = DateTime.UtcNow + _timeout;
            if (!process.WaitForExit(_timeout) || !Task.WaitAll([stdout, stderr], Remaining(deadline)))
            {
                process.Kill(entireProcessTree: true);
                return new CommandResult(-1, $"timed out after {_timeout.TotalSeconds} seconds");
            }
            return new CommandResult(process.ExitCode, stdout.Result, stderr.Result);
        }
        private static TimeSpan Remaining(DateTime deadline)
        {
            var left = deadline - DateTime.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }
}
