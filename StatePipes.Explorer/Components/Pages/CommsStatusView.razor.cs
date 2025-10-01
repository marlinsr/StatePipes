using Microsoft.AspNetCore.Components;
using StatePipes.Messages;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class CommsStatusView
    {
        [Parameter]
        public bool IsConnectedToBroker { get; set; }
        [Parameter]
        public bool IsConnectedToService { get; set; }
        [Parameter]
        public AllStatePipesProxyStatusEvent? CommStatusEvent { get; set; }
        [Parameter]
        public string? TabTitle { get; set; }
        [Parameter]
        public EventCallback<bool> OnCollapsedChanged { get; set; }
        private async void Collapsed(bool showing)
        {
            await OnCollapsedChanged.InvokeAsync(showing);
        }

    }
}
