using StatePipes.ServiceCreatorTool;

namespace StatePipes.Test.ServiceCreatorTool
{
    /// <summary>A dialog prompt that fails the test if any dialog opens, so headless tests prove no UI was shown.</summary>
    internal sealed class FailingDialogs : IDialogPrompt
    {
        public string? Text(string prompt, string initial) => Fail(prompt);
        public string? Choose(IReadOnlyList<string> options, string prompt) => Fail(prompt);
        public string? File(string prompt, string filter, string? initialDirectory) => Fail(prompt);
        public string? Folder(string prompt) => Fail(prompt);
        public bool? Confirm(string prompt) { Fail(prompt); return null; }
        private static string? Fail(string prompt) { Assert.Fail($"dialog opened: {prompt}"); return null; }
    }

    /// <summary>A dialog prompt that answers every dialog with preset values; null means the user canceled.</summary>
    internal sealed class StubDialogs(string? text = null, string? choice = null, bool? confirm = null) : IDialogPrompt
    {
        public string? Text(string prompt, string initial) => text;
        public string? Choose(IReadOnlyList<string> options, string prompt) => choice;
        public string? File(string prompt, string filter, string? initialDirectory) => text;
        public string? Folder(string prompt) => text;
        public bool? Confirm(string prompt) => confirm;
    }

    internal static class ToolTestHelpers
    {
        public static ToolInput Headless(params (string Arg, string Value)[] args) =>
            new(args.ToDictionary(a => a.Arg, a => a.Value, StringComparer.OrdinalIgnoreCase), true, new FailingDialogs());

        public static string Capture(Action action)
        {
            var original = Console.Out;
            using var writer = new StringWriter();
            Console.SetOut(writer);
            try { action(); }
            finally { Console.SetOut(original); }
            return writer.ToString();
        }
    }
}
