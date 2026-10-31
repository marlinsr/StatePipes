namespace StatePipes.ServiceCreatorTool
{
    /// <summary>The dialogs a generator can open. Each returns null when the user cancels.</summary>
    internal interface IDialogPrompt
    {
        string? Text(string prompt, string initial);
        string? Choose(IReadOnlyList<string> options, string prompt);
        string? File(string prompt, string filter, string? initialDirectory);
        string? Folder(string prompt);
        bool? Confirm(string prompt);
    }

    internal sealed class SelectionDialogPrompt : IDialogPrompt
    {
        public string? Text(string prompt, string initial)
        {
            var value = initial;
            return SelectionDialog.ShowInputDialog(ref value, prompt) == DialogResult.OK && !string.IsNullOrEmpty(value) ? value : null;
        }
        public string? Choose(IReadOnlyList<string> options, string prompt) => SelectionDialog.ShowListSelection([.. options], prompt);
        public string? File(string prompt, string filter, string? initialDirectory) =>
            SelectionDialog.SelectFile(out var fileName, prompt, filter, initialDirectory) ? fileName : null;
        public string? Folder(string prompt)
        {
            using FolderBrowserDialog dialog = new() { Description = prompt };
            return dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath) ? dialog.SelectedPath : null;
        }
        public bool? Confirm(string prompt) => SelectionDialog.GetUserConfirmation(prompt);
    }
}
