using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using StatePipes.Common;
using StatePipes.Explorer.NonWebClasses;
using StatePipes.Messages;
using StatePipes.ProcessLevelServices;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.Explorer.Components.Pages
{
    /// <summary>WORKFLOW tab: asks the service for every running workflow with a correlationId and draws each one.</summary>
    public partial class WorkflowDiagramViewer
    {
        private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan TabShowDelay = TimeSpan.FromMilliseconds(150);
        private readonly Lock _pollLock = new();
        private IJSObjectReference? _diagramModule;
        private Timer? _pollTimer;
        private string? _input;
        private string? _pendingCorrelationId;
        private EventEntry? _snapshot;
        private DateTime _sentAt;
        private WorkflowDiagramsEvent? _result;
        private string _status = string.Empty;
        private bool _needsDraw;
        private bool _delayDraw;
        private TabEnum _prevTabId;

        [Parameter]
        public Guid InstanceGuid { get; set; }
        [Parameter]
        public TabEnum TabId { get; set; }

        private bool CanInterrogate => WorkflowInterrogation.NormalizeInput(_input) != null && _pendingCorrelationId == null
            && _statePipesHandler.GetIsConnectedToBroker(InstanceGuid);

        protected override void OnParametersSet()
        {
            if (TabId == TabEnum.Workflow && _prevTabId != TabEnum.Workflow && _result != null)
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
            try
            {
                _snapshot = LatestReply();
                _pendingCorrelationId = id;
                _sentAt = DateTime.UtcNow;
                _status = "Waiting for reply…";
                _statePipesHandler.SendCommand(InstanceGuid, typeof(GetWorkflowDiagramsCommand).FullName!,
                    JsonUtility.GetJsonStringForObject(new GetWorkflowDiagramsCommand(id)));
                _pollTimer = new Timer(async _ => await Poll(), null, PollInterval, PollInterval);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
                _pendingCorrelationId = null;
                _status = ex.Message;
            }
        }

        private EventEntry? LatestReply() =>
            _statePipesHandler.GetEventJsons(InstanceGuid).FirstOrDefault(e => e.FullName == typeof(WorkflowDiagramsEvent).FullName);

        private async Task Poll()
        {
            if (!_pollLock.TryEnter()) return;
            try
            {
                var pending = _pendingCorrelationId;
                if (pending == null) return;
                var candidate = LatestReply();
                if (WorkflowInterrogation.IsMatchingReply(candidate, _snapshot, pending))
                {
                    _result = WorkflowInterrogation.ToEvent(candidate!);
                    _status = WorkflowInterrogation.StatusText(pending, _result?.Workflows.Count ?? 0);
                    _needsDraw = true;
                    _delayDraw = false;
                }
                else if (DateTime.UtcNow - _sentAt >= ReplyTimeout)
                {
                    _status = "No reply after 10 s";
                }
                else return;
                _pendingCorrelationId = null;
                StopTimer();
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex) { Log?.LogException(ex); }
            finally { _pollLock.Exit(); }
        }

        private async Task DrawAll()
        {
            _needsDraw = false;
            var result = _result;
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
