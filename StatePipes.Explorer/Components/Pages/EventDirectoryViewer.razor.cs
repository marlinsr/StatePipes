using Microsoft.AspNetCore.Components;
using StatePipes.Explorer.NonWebClasses;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class EventDirectoryViewer
    {
        [Parameter]
        public Guid InstanceGuid { get; set; } = Guid.Empty;
        [Parameter]
        public DirectoryListForEvents? Directories { get; set; }
        [Parameter]
        public string TitleColor { get; set; } = DarkTitleColor;
        [Parameter]
        public string? QuickViewUriPrefix { get; set; }
        [Parameter]
        public ExcludeAndIncludeLists? ObjectEditorFilter { get; set; }
        [Parameter]
        public ExcludeAndIncludeLists? ShowFilter { get; set; }
        public string QuickViewString
        {
            get
            {
                ExcludeAndIncludeLists filter = new();
                filter.Exclude.Add(ExcludeAndIncludeLists.ExcludeAllString);
                filter.Include.Add(Directories?.Event?.FullName!);
                return $"{QuickViewUriPrefix}_{Directories?.Event?.FullName}/{filter.GetJsonString()}";
            }
        }
        public const string LightTitleColor = "blue";
        public const string DarkTitleColor = "darkblue";
        private string _otherColor = LightTitleColor;
        private string GetOtherColor(string originalColor) => originalColor == LightTitleColor ? DarkTitleColor : LightTitleColor;
        protected override void OnParametersSet()
        {
            _otherColor = GetOtherColor(TitleColor);
        }
    }
}
