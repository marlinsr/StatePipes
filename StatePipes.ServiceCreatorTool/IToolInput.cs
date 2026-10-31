namespace StatePipes.ServiceCreatorTool
{
    /// <summary>
    /// Every input a generator asks for. Each call names the command-line argument that can supply the value; without it
    /// the value comes from a dialog, or in headless mode the call reports the missing argument. A null result means stop
    /// without writing any file.
    /// </summary>
    internal interface IToolInput
    {
        bool Headless { get; }
        bool Supplied(string arg);
        string? Text(string arg, string prompt, string initial = "");
        string? Name(string arg, string prompt);
        string? Choose(string arg, IReadOnlyList<string> options, string prompt, bool fromBuild = false);
        string? File(string arg, string prompt, string filter, string? initialDirectory = null);
        string? Folder(string arg, string prompt);
        bool? Confirm(string arg, string trueValue, string falseValue, string prompt);
        int? PositiveInt(string arg, string prompt);
        IToolInput WithDefault(string arg, string value);
    }
}
