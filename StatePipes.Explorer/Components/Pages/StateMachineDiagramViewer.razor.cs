using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StatePipes.Common;
using StatePipes.Explorer.NonWebClasses;
using StatePipes.Messages;
using StatePipes.ProcessLevelServices;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Explorer.Components.Pages
{
    public partial class StateMachineDiagramViewer 
    {
        private System.Threading.Timer? _timer;
        private List<EventEntry> _eventJsonDictionary = [];
        private string[] _stateMachineDiagramsStrings = [];
        private string[] _prevStateMachineDiagramsStrings = [];
        private string _getAllStateMachineDiagsCmdJson = string.Empty;
        private string _getAllStateMachineDiagsCmdFullName = string.Empty;
        private StateMachineDiagramsEvent? _stateMachineDiagramsEvent;
        private object _stateMachineDiagramsLock = new();
        private IJSObjectReference? _diagramModule;
        private bool _isConnectedToBroker;
        [Parameter]
        public Guid InstanceGuid { get; set; }
        [Parameter]
        public TabEnum TabId { get; set; }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    _diagramModule = await JsRuntime.InvokeAsync<IJSObjectReference>("import", "/diagrams.js");
                }
                catch (Exception e)
                {
                    LoggerHolder.Log?.LogException(e);
                }
                _getAllStateMachineDiagsCmdJson = JsonUtility.GetJsonStringForObject(new GetAllStateMachineDiagramsCommand());
                _getAllStateMachineDiagsCmdFullName = typeof(GetAllStateMachineDiagramsCommand).FullName!;
                _timer = new System.Threading.Timer(async _ => { await PeriodicHandle(); }, null, 0, 1000);
            }
            await base.OnAfterRenderAsync(firstRender);
        }
        private async Task PeriodicHandle()
        {
            try
            {
                _eventJsonDictionary = _statePipesHandler.GetEventJsons(InstanceGuid);
                _isConnectedToBroker = _statePipesHandler.GetIsConnectedToBroker(InstanceGuid);
                await HandleStateMachineTab();
            }
            catch (Exception ex) { Log?.LogException(ex); }
        }
        private async Task HandleStateMachineTab()
        {
            if (!_isConnectedToBroker || TabId != TabEnum.StateMachine) return;
            if (!Monitor.TryEnter(_stateMachineDiagramsLock, TimeSpan.Zero)) return;
            try
            {
                PopulateStateMachines();
                _statePipesHandler.SendCommand(InstanceGuid, _getAllStateMachineDiagsCmdFullName, _getAllStateMachineDiagsCmdJson);
                if (HasStateMachineDiagramsChanged())
                {
                    await InvokeAsync(StateHasChanged);
                    DrawStateMachines();
                }
            }
            catch (Exception e) { Log?.LogException(e); }
            finally { Monitor.Exit(_stateMachineDiagramsLock); }
        }
        private void PopulateStateMachines()
        {
            var eventVar = _eventJsonDictionary.FirstOrDefault(c => c.FullName == typeof(StateMachineDiagramsEvent).FullName);
            if (eventVar?.Obj != null)
            {
                _stateMachineDiagramsEvent = JsonUtility.CloneToType<StateMachineDiagramsEvent>(eventVar.Obj)!;
                _stateMachineDiagramsStrings = _stateMachineDiagramsEvent?.Diagrams.ToArray() ?? [];
            }
            else
            {
                _stateMachineDiagramsEvent = null;
                _stateMachineDiagramsStrings = [];
            }
        }
        private async void DrawStateMachine(string dotString, string renderTo)
        {
            try
            {
                if (_diagramModule == null) return;
                await _diagramModule.InvokeVoidAsync("renderDot", dotString, renderTo);
            }
            catch (Exception e)
            {
                LoggerHolder.Log?.LogException(e);
            }
        }
        private void DrawStateMachines()
        {
            var stateMachineDiagramsStrings = _stateMachineDiagramsStrings;
            if (_diagramModule != null)
            {
                for (int i = 0; i < stateMachineDiagramsStrings.Length; i++)
                {
                    if (!string.IsNullOrEmpty(stateMachineDiagramsStrings[i]))
                    {
                        var divId = $"StateMachine{i}";
                        DrawStateMachine(stateMachineDiagramsStrings[i], divId);
                    }
                }
                _prevStateMachineDiagramsStrings = _stateMachineDiagramsStrings;
            }
            _prevStateMachineDiagramsStrings = _stateMachineDiagramsStrings;
        }
        private bool HasStateMachineDiagramsChanged()
        {
            string[] stateMachineDiagramsStrings = _stateMachineDiagramsStrings;
            string[] prevStateMachineDiagramsStrings = _prevStateMachineDiagramsStrings;
            if (stateMachineDiagramsStrings.Length != prevStateMachineDiagramsStrings.Length) return true;
            for (int i = 0; i < stateMachineDiagramsStrings.Length; i++)
            {
                if (stateMachineDiagramsStrings[i] != prevStateMachineDiagramsStrings[i]) return true;
            }
            return false;
        }
        public async ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            _timer?.Dispose();
            _timer = null;
            if (_diagramModule is not null) try { await _diagramModule.DisposeAsync(); } catch(Exception ex) { Log?.LogException(ex); }
            _diagramModule = null;
        }
    }
}
