using System.Text.RegularExpressions;

namespace StatePipes.ServiceCreatorTool
{
    /// <summary>
    /// Uses a supplied argument when there is one. Otherwise headless mode reports the missing argument, and Visual Studio
    /// mode opens the dialog.
    /// </summary>
    internal sealed partial class ToolInput(IReadOnlyDictionary<string, string> args, bool headless, IDialogPrompt dialogs) : IToolInput
    {
        private readonly Dictionary<string, string> _args = new(args, StringComparer.OrdinalIgnoreCase);
        public static ToolInput Dialogs() => new(new Dictionary<string, string>(), false, new SelectionDialogPrompt());
        public bool Headless => headless;
        public bool Supplied(string arg) => !string.IsNullOrEmpty(Value(arg));
        public string? Text(string arg, string prompt, string initial = "") => Get(arg, () => dialogs.Text(prompt, initial));
        public string? Name(string arg, string prompt)
        {
            var value = Text(arg, prompt);
            if (value == null || IdentifierRegex().IsMatch(value)) return value;
            Console.WriteLine($"{arg} must be a C# identifier: {value}.");
            return null;
        }
        public string? Choose(string arg, IReadOnlyList<string> options, string prompt, bool fromBuild = false)
        {
            if (options.Count == 0) return null;
            var value = Get(arg, () => dialogs.Choose(options, prompt));
            if (value == null) return null;
            var match = options.FirstOrDefault(o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
            var hint = fromBuild ? ". If it was just added, build the solution first." : string.Empty;
            Console.WriteLine($"{arg} {value} not found; one of: {string.Join(", ", options)}{hint}");
            return null;
        }
        public string? File(string arg, string prompt, string filter, string? initialDirectory = null) =>
            Get(arg, () => dialogs.File(prompt, filter, initialDirectory));
        public string? Folder(string arg, string prompt) => Get(arg, () => dialogs.Folder(prompt));
        public bool? Confirm(string arg, string trueValue, string falseValue, string prompt)
        {
            if (!Supplied(arg)) return Get<bool?>(arg, () => dialogs.Confirm(prompt));
            var value = Value(arg);
            if (string.Equals(value, trueValue, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, falseValue, StringComparison.OrdinalIgnoreCase)) return false;
            Console.WriteLine($"{arg} must be {trueValue} or {falseValue}.");
            return null;
        }
        public int? PositiveInt(string arg, string prompt)
        {
            var value = Text(arg, prompt);
            if (value == null) return null;
            if (int.TryParse(value, out var number) && number > 0) return number;
            Console.WriteLine($"{arg} must be a positive number of milliseconds.");
            return null;
        }
        public IToolInput WithDefault(string arg, string value)
        {
            if (Supplied(arg)) return this;
            return new ToolInput(new Dictionary<string, string>(_args, StringComparer.OrdinalIgnoreCase) { [arg] = value }, headless, dialogs);
        }
        private string? Value(string arg) => _args.TryGetValue(arg, out var value) ? value : null;
        private string? Get(string arg, Func<string?> dialog) => Supplied(arg) ? Value(arg) : Get<string?>(arg, dialog);
        private T? Get<T>(string arg, Func<T?> dialog)
        {
            if (headless)
            {
                Console.WriteLine($"Headless mode needs {arg}.");
                return default;
            }
            var value = dialog();
            if (value == null) Console.WriteLine("Action canceled.");
            return value;
        }
        [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
        private static partial Regex IdentifierRegex();
    }
}
