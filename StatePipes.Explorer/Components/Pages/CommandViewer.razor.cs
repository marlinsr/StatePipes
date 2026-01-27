using Microsoft.AspNetCore.Components;
using StatePipes.Common;
using StatePipes.Explorer.NonWebClasses;
using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class CommandViewer
    {
        [Parameter]
        public DirectoryListForCommands? Directories { get; set; }
        [Parameter]
        public Guid InstanceGuid { get; set; } = Guid.Empty;
        [Parameter]
        public string TitleColor { get; set; } = "darkgreen";
        [Parameter]
        public string? QuickViewUriPrefix { get; set; }
        [Parameter]
        public ExcludeAndIncludeLists? ObjectEditorFilter { get; set; }
        [Parameter]
        public ExcludeAndIncludeLists? ShowFilter { get; set; }


        private string QuickViewString
        {
            get
            {
                ExcludeAndIncludeLists filter = new();
                filter.Exclude.Add(ExcludeAndIncludeLists.ExcludeAllString);
                filter.Include.Add(Directories?.Command?.FullName!);
                var ret = $"{QuickViewUriPrefix}_{Directories?.Command?.FullName}/{filter.GetJsonString()}";
                if (!(ObjectEditorFilter?.IsIncluded(Directories?.Command?.FullName!) ?? true))
                {
                    ExcludeAndIncludeLists objFilter = new();
                    objFilter.Exclude.Add(Directories?.Command?.FullName!);
                    ret += $"/{objFilter.GetJsonString()}";
                }
                return ret;
            }
        }

        private ClassViewer _classEditor = default!;
#pragma warning disable IDE1006 // Naming Styles
        private int periodicSendInSeconds { get; set; } = 0;
#pragma warning restore IDE1006 // Naming Styles
#pragma warning disable IDE1006 // Naming Styles
        private bool stopPeriodic { get; set; }
#pragma warning restore IDE1006 // Naming Styles
        private System.Threading.Timer? timer;
        private bool ShowObjectEditor = true;
        private PropertyValueClass? CommandObject;
        private string? CommandObjectString = string.Empty;

        private void SendCommand()
        {
            if (ShowObjectEditor && (ObjectEditorFilter?.IsIncluded(Directories?.Command?.FullName!) ?? true))
            {
                CommandObjectString = FormattedCommandJson();
            }
            if (Directories?.Command != null && CommandObjectString != null) _statePipesHandler.SendCommand(InstanceGuid, Directories.Command.FullName, CommandObjectString);
        }

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (string.IsNullOrEmpty(CommandObjectString))
            {
                CommandObjectString = Directories?.Command?.Json;
                CommandObject = GetCommandObject(CommandObjectString);
            }
        }

        private void ToggleDisplayMode(bool showObjectEditor)
        {
            if (ShowObjectEditor == showObjectEditor) return;
            ShowObjectEditor = showObjectEditor;
            if (ShowObjectEditor)
            {
                CommandObject = GetCommandObject(CommandObjectString);
            }
            else
            {
                CommandObjectString = FormattedCommandJson();
            }
        }

        private void HandleDivCollapse()
        {
            CommandObjectString = FormattedCommandJson();
            CommandObject = GetCommandObject(CommandObjectString);
            StateHasChanged();
        }

        private void ResetJson()
        {
            if (Directories?.Command?.FullName != null)
            {
                _statePipesHandler.ResetJson(InstanceGuid, Directories.Command.FullName);
                CommandObjectString = Directories.Command.OriginalJson;
                CommandObject = GetCommandObject(CommandObjectString);
            }
        }

        private void StartStopTimer()
        {
            if (stopPeriodic || periodicSendInSeconds <= 0 || CommandObjectString == null)
            {
                timer?.Dispose();
                timer = null;
            }
            else
            {
                timer = new System.Threading.Timer(SendCommandOnTimer, null, 0, periodicSendInSeconds * 1000);
            }
        }

        private void SendCommandOnTimer(object? state)
        {
            if (!string.IsNullOrEmpty(CommandObjectString)) _statePipesHandler.SendCommand(InstanceGuid, Directories!.Command!.FullName, CommandObjectString);
        }
        private string FormattedCommandJson()
        {
            StringBuilder jsonStringBuilder = new();
            if (!_classEditor?.GetJson(jsonStringBuilder) ?? false) return string.Empty;
            var obj = GetCommandObject(jsonStringBuilder.ToString());
            if (obj == null || obj.Value == null) return string.Empty;
            return JsonUtility.GetJsonStringForObject(obj.Value);
        }

        private PropertyValueClass? GetCommandObject(string? json)
        {
            if (json == null) return null;
            if (Directories?.Command == null) return null;
            var obj = _statePipesHandler.GetCommandObject(InstanceGuid, Directories.Command.FullName, json);
            if (obj == null) return null;
            return PropertyEntityViewer.GetPropertyValueClass(InstanceGuid, Directories.Command.FullName, null, obj.GetType(), obj, false);
        }

    }
}
