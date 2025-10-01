using Microsoft.AspNetCore.Components;
using StatePipes.Explorer.NonWebClasses;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class CollapsableDivision 
    {
        [Parameter]
        public RenderFragment? ChildContent { get; set; }
        [Parameter]
        public string TitleName { get; set; } = string.Empty;
        [Parameter]
        public string Timestamp { get; set; } = string.Empty;
        [Parameter]
        public string TitleColor { get; set; } = "black";
        [Parameter]
        public bool ShowExpandChildren { get; set; }
        [Parameter]
        public EventCallback<bool> OnCollapsedChanged { get; set; }
        [Parameter]
        public ExcludeAndIncludeLists? Filter { get; set; }
        [Parameter]
        public string? Namespace { get; set; } = string.Empty;

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
        }

        private string ChildrenCollapseText => IsExpanded ? "Collapse All" : "Expand All";

        private string CollapseText => IsExpanded ? "-" : "+";

        private async void ToggleCollapse()
        {
            //only do on collapse
            SetChildrenCollapse(IsExpanded, false);
            await OnCollapsedChanged.InvokeAsync(IsExpanded);
        }

        private void ToggleChildrenCollapse()
        {
            SetChildrenCollapse(IsExpanded, true);
        }

        private void Include()
        {
            if (!string.IsNullOrEmpty(Namespace)) Filter?.Include.Add(Namespace);
        }

        private void Exclude()
        {
            if (!string.IsNullOrEmpty(Namespace)) Filter?.Exclude.Add(Namespace);
        }
    }
}
