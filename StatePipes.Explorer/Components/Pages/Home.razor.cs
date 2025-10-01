using Microsoft.AspNetCore.Components;
using StatePipes.Common;
using StatePipes.Common.Internal;
using StatePipes.Explorer.NonWebClasses;
using StatePipes.Messages;
using StatePipes.ProcessLevelServices;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Explorer.Components.Pages
{
    public partial class Home
    {
        private const string amqpsPrefix = "amqps://";
        private System.Threading.Timer? _timer;
        private List<EventEntry> _eventJsonDictionary = [];
        private List<CommandEntry> _commandList = [];
        private bool _isConnectedToBroker;
        private bool _isConnectedToService;
        private string? _brokerUri;
        private string _getLogFileCmdJson = string.Empty;
        private string _getLogFileCmdFullName = string.Empty;
        private string _getAllClientStatusCmdJson = string.Empty;
        private string _getAllClientStatusCmdFullName = string.Empty;
        private LogFileTailEvent? _logFileEvent;
        private TabEnum _showTab = TabEnum.ObjectEditor;
        private bool _showCommunicationsStatus = false;
        private AllStatePipesProxyStatusEvent? _commStatusEvent;
        private ExcludeAndIncludeLists _showFilter = new();
        private ExcludeAndIncludeLists _objectEditorFilter = new();
        private Guid InstanceGuid { get; } = Guid.NewGuid();
        [Parameter]
        public string? BrokerUriParameter { get; set; }
        [Parameter]
        public string? ExchangeName { get; set; }
        [Parameter]
        public string? ClientCertFileName { get; set; }
        [Parameter]
        public string? ClientCertPasswordFileName { get; set; }
        [Parameter]
        public string? TabTitle { get; set; }
        [Parameter]
        public string? ShowFilter { get; set; }
        [Parameter]
        public string? ObjectEditorFilter { get; set; }
        private string? ClientCertPassword { get; set; }
        private string Time { get; set; } = string.Empty;
        private string LogText { get => _logFileEvent?.LogFileTail ?? string.Empty; set { } }
        private string AppendParameter(string? val)
        {
            string ret = "/";
            if (!string.IsNullOrEmpty(val)) ret += val.Replace("/", "%2F");
            return ret;
        }
        private string QuickViewUriPrefix
        {
            get
            {
                var urlParts = NavManager.Uri.ToString().Split('/').ToList();
                string quickViewUriPrefix = $"{urlParts[0]}//{urlParts[2]}/Params";
                quickViewUriPrefix += AppendParameter(BrokerUri);
                quickViewUriPrefix += AppendParameter(ExchangeName);
                quickViewUriPrefix += AppendParameter(ClientCertFileName);
                quickViewUriPrefix += AppendParameter(ClientCertPasswordFileName);
                quickViewUriPrefix += AppendParameter(TabTitle);
                return quickViewUriPrefix;
            }
        }
        private string? BrokerUri
        {
            get
            {
                if (string.IsNullOrEmpty(_brokerUri) && !string.IsNullOrEmpty(BrokerUriParameter)) _brokerUri = Uri.UnescapeDataString(BrokerUriParameter);
                return _brokerUri;
            }
            set => _brokerUri = value;
        }
        private void ShowingCommunicationStatus(bool expanded) => _showCommunicationsStatus = expanded;
        private void ToggleDisplayMode(TabEnum show)
        {
            if (_showTab == show) return;
            _showTab = show;
        }
        protected override async Task OnInitializedAsync()
        {
            _getLogFileCmdJson = JsonUtility.GetJsonStringForObject(new GetLogFileTailCommand());
            _getLogFileCmdFullName = typeof(GetLogFileTailCommand).FullName!;
            _getAllClientStatusCmdJson = JsonUtility.GetJsonStringForObject(new GetAllStatePipesProxyStatusCommand());
            _getAllClientStatusCmdFullName = typeof(GetAllStatePipesProxyStatusCommand).FullName!;
            await base.OnInitializedAsync();
            if (string.IsNullOrEmpty(BrokerUri)) BrokerUri = amqpsPrefix;
            _showFilter = ExcludeAndIncludeLists.GetFromJson(ShowFilter ?? string.Empty);
            _objectEditorFilter = ExcludeAndIncludeLists.GetFromJson(ObjectEditorFilter ?? string.Empty);
            _timer = new System.Threading.Timer(async _ => { await PeriodicHandle(); }, null, 0, 1000);
            Time = "Not talking to server";
        }
        private void HandleLogFileTab()
        {
            if (_showTab != TabEnum.LogFile || !_isConnectedToBroker) return;
            PopulateLogFileText();
            _statePipesHandler.SendCommand(InstanceGuid, _getLogFileCmdFullName, _getLogFileCmdJson);
        }
        private void HandleCommunicationsStatusDiv()
        {
            if (!_showCommunicationsStatus) return;
            PopulateCommunicationStatus();
            _statePipesHandler.SendCommand(InstanceGuid, _getAllClientStatusCmdFullName, _getAllClientStatusCmdJson);
        }
        private async Task PeriodicHandle()
        {
            try
            {
                Time = DateTime.Now.ToString();
                _eventJsonDictionary = _statePipesHandler.GetEventJsons(InstanceGuid);
                _commandList = _statePipesHandler.GetCommandList(InstanceGuid);
                _isConnectedToBroker = _statePipesHandler.GetIsConnectedToBroker(InstanceGuid);
                HandleCommunicationsStatusDiv();
                HandleLogFileTab();
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex) { Log?.LogException(ex); }
        }
        private void PopulateLogFileText()
        {
            var eventVar = _eventJsonDictionary.FirstOrDefault(c => c.FullName == typeof(LogFileTailEvent).FullName);
            if (eventVar?.Obj != null)
            {
                _logFileEvent = JsonUtility.CloneToType<LogFileTailEvent>(eventVar.Obj)!;
            }
        }
        private void PopulateCommunicationStatus()
        {
            var eventVar = _eventJsonDictionary.FirstOrDefault(c => c.FullName == typeof(AllStatePipesProxyStatusEvent).FullName);
            if (eventVar?.Obj != null)
            {
                _commStatusEvent = JsonUtility.CloneToType<AllStatePipesProxyStatusEvent>(eventVar.Obj)!;
                var eventTimestamp = DateTime.Parse(eventVar.Timestamp);
                _isConnectedToService = (DateTime.Now - eventTimestamp).TotalSeconds < 3.00;
                if (!_isConnectedToService) _commStatusEvent = null;
            }
            else
            {
                _commStatusEvent = null;
                _isConnectedToService = false;
            }
        }
        private void InitializeComms(string dummyParam)
        {
            var hashedPassword = string.IsNullOrEmpty(ClientCertPassword) ? ClientCertPassword : PasswordHasher.HashPassword(ClientCertPassword);
            if (!string.IsNullOrEmpty(BrokerUri) && !string.IsNullOrEmpty(ExchangeName) && !string.IsNullOrEmpty(ClientCertFileName) && !string.IsNullOrEmpty(ClientCertPasswordFileName) && !string.IsNullOrEmpty(hashedPassword))
            {
                ClientCertPassword = string.Empty;
                _statePipesHandler.Initialize(InstanceGuid, BrokerUri!, ExchangeName!, _showFilter, ClientCertFileName, ClientCertPasswordFileName, hashedPassword);
            }
        }
        private List<CommandEntry> GetNonExcludedCommands()
        {
            var results = _commandList.Where(c => _showFilter.IsIncluded(c.FullName)).ToList();
            return results;
        }
        private List<EventEntry> GetNonExcludedEvents()
        {
            var results = _eventJsonDictionary.Where(c => _showFilter.IsIncluded(c.FullName)).ToList();
            return results;
        }
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            try
            {
                _timer?.Dispose();
                _timer = null;
                //SRM Check close if was not initialized
                _statePipesHandler.Close(InstanceGuid);
            }
            catch (Exception e)
            {
                LoggerHolder.Log?.LogException(e);
                LoggerHolder.Log?.Flush();
            }
        }
    }
}
