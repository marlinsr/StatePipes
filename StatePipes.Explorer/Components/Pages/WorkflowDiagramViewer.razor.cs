using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using StatePipes.Common;
using StatePipes.Explorer.NonWebClasses;
using StatePipes.Interfaces;
using StatePipes.Messages;
using StatePipes.ProcessLevelServices;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.Explorer.Components.Pages
{
    /// <summary>
    /// WORKFLOW tab: asks the service for every running workflow with a correlationId and draws each one.
    /// All session state is read and written on the Blazor renderer context; the poll timer only marshals onto it.
    /// </summary>
    public partial class WorkflowDiagramViewer
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan TabShowDelay = TimeSpan.FromMilliseconds(150);
        private readonly WorkflowInterrogationSession _session = new();
        private IJSObjectReference? _diagramModule;
        private Timer? _pollTimer;
        private string? _input;
        private bool _needsDraw;
        private bool _delayDraw;
        private TabEnum _prevTabId;

        [Parameter]
        public Guid InstanceGuid { get; set; }
        [Parameter]
        public TabEnum TabId { get; set; }

        private WorkflowDiagramsEvent? Result => _session.Result;

        private bool CanInterrogate => WorkflowInterrogation.NormalizeInput(_input) != null && !_session.IsPending
            && _statePipesHandler.GetIsConnectedToBroker(InstanceGuid);

        protected override void OnParametersSet()
        {
            if (TabId == TabEnum.Workflow && _prevTabId != TabEnum.Workflow && Result != null)
            {
                _needsDraw = true;
                _delayDraw = true;
            }
            _prevTabId = TabId;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try { _diagramModule = await JsRuntime.InvokeAsync<IJSObjectReference>("import", "/diagrams.js"); }
                catch (Exception e) { Log?.LogException(e); }
            }
            if (_needsDraw) await DrawAll();
            await base.OnAfterRenderAsync(firstRender);
        }

        private void OnKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter") Interrogate();
        }

        private void Interrogate()
        {
            var id = WorkflowInterrogation.NormalizeInput(_input);
            if (id == null || !CanInterrogate) return;
            StopTimer();
            try
            {
                _session.Start(id, LatestReply(), DateTime.UtcNow);
                _statePipesHandler.SendCommand(InstanceGuid, typeof(GetWorkflowDiagramsCommand).FullName!,
                    JsonUtility.GetJsonStringForObject(new GetWorkflowDiagramsCommand(id)));
                _pollTimer = new Timer(OnPollTimer, null, PollInterval, PollInterval);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
                _session.Fail(ex.Message);
            }
        }

        private IEvent? LatestReply() =>
            _statePipesHandler.GetEventJsons(InstanceGuid).FirstOrDefault(e => e.FullName == typeof(WorkflowDiagramsEvent).FullName)?.Obj;

        private async void OnPollTimer(object? _)
        {
            try { await InvokeAsync(Poll); }
            catch (Exception ex) { Log?.LogException(ex); }
        }

        /// <summary>Runs on the renderer context, so it is serialized with Interrogate and rendering.</summary>
        private void Poll()
        {
            if (!_session.Tick(LatestReply(), DateTime.UtcNow)) return;
            StopTimer();
            if (_session.Result != null)
            {
                _needsDraw = true;
                _delayDraw = false;
            }
            StateHasChanged();
        }

        private async Task DrawAll()
        {
            _needsDraw = false;
            var result = Result;
            if (_diagramModule == null || result == null) return;
            if (_delayDraw)
            {
                _delayDraw = false;
                await Task.Delay(TabShowDelay); // let the tab pane become visible so graphviz can size to it
            }
            for (int i = 0; i < result.Workflows.Count; i++)
            {
                var dot = result.Workflows[i].DotGraph;
                if (string.IsNullOrEmpty(dot)) continue;
                try { await _diagramModule.InvokeVoidAsync("renderDot", dot, $"WorkflowDiagram{i}"); }
                catch (Exception e) { LoggerHolder.Log?.LogException(e); }
            }
        }

        private void StopTimer()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        public async ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            StopTimer();
            if (_diagramModule is not null) try { await _diagramModule.DisposeAsync(); } catch (Exception ex) { Log?.LogException(ex); }
            _diagramModule = null;
        }
    }
}
