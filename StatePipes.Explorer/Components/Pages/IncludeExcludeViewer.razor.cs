using Microsoft.AspNetCore.Components;
using StatePipes.Explorer.NonWebClasses;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class IncludeExcludeViewer
    {
        [Parameter]
        public List<string>? ItemList { get; set; }
        [Parameter]
        public string Title { get; set; } = string.Empty;
        [Parameter]
        public EventCallback<string> OnListChanged { get; set; }
        [Parameter]
        public bool AddAll { get; set; }

        private string NamespaceEntry { get; set; } = string.Empty;

        private async void AddElement()
        {
            if (string.IsNullOrEmpty(NamespaceEntry)) return;
            ItemList?.Add(NamespaceEntry);
            await OnListChanged.InvokeAsync(string.Empty);
            StateHasChanged();
        }
        private async void AddAllElement()
        {
            ItemList?.Add(ExcludeAndIncludeLists.ExcludeAllString);
            await OnListChanged.InvokeAsync(string.Empty);
            StateHasChanged();
        }
        private async void DeleteElement()
        {
            if (ItemList == null || ItemList.Count <= 0) return;
            ItemList?.RemoveAt(ItemList.Count - 1);
            await OnListChanged.InvokeAsync(string.Empty);
            StateHasChanged();
        }
        private async void ClearElements()
        {
            if (ItemList == null || ItemList.Count <= 0) return;
            ItemList?.Clear();
            await OnListChanged.InvokeAsync(string.Empty);
            StateHasChanged();
        }

    }
}
