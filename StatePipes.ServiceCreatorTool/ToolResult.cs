namespace StatePipes.ServiceCreatorTool
{
    /// <summary>What a Create operation did: whether it succeeded, and the file to open in Visual Studio, if any.</summary>
    internal sealed record ToolResult(bool Succeeded, string? FileToOpen = null)
    {
        public static ToolResult Ok(string? fileToOpen = null) => new(true, string.IsNullOrEmpty(fileToOpen) ? null : fileToOpen);
        public static readonly ToolResult Failed = new(false);
    }
}
