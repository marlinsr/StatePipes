using Microsoft.AspNetCore.Components;
using StatePipes.Explorer.NonWebClasses;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class CommandDirectoryViewer
    {
        [Parameter]
        public DirectoryListForCommands? Directories { get; set; }
        [Parameter]
        public Guid InstanceGuid { get; set; } = Guid.Empty;
        [Parameter]
        public string TitleColor { get; set; } = DarkTitleColor;
        [Parameter]
        public string? QuickViewUriPrefix { get; set; }
        [Parameter]
        public ExcludeAndIncludeLists? ObjectEditorFilter { get; set; }
        [Parameter]
        public ExcludeAndIncludeLists? ShowFilter { get; set; }

        public const string LightTitleColor = "green";
        public const string DarkTitleColor = "darkgreen";
        private string _otherColor = LightTitleColor;
        private string GetOtherColor(string originalColor) => originalColor == LightTitleColor ? DarkTitleColor : LightTitleColor;

        protected override void OnParametersSet()
        {
            _otherColor = GetOtherColor(TitleColor);
            base.OnParametersSet();
        }
    }
}
