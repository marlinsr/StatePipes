using Microsoft.AspNetCore.Components;
using StatePipes.Explorer.NonWebClasses;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class FilterViewer
    {
        [Parameter]
        public ExcludeAndIncludeLists? Filter { get; set; }
        [Parameter]
        public string Title { get; set; } = string.Empty;
        [Parameter]
        public EventCallback<string> OnListChanged { get; set; }

    }
}
