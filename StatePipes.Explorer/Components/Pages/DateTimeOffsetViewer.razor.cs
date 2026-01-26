using Microsoft.AspNetCore.Components;
using StatePipes.Common;
using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class DateTimeOffsetViewer
    {
        private PropertyValueClass? PreviousEditorObject;
        private DateOnly _newDate = new();
        private TimeOnly _newTime = new();
        private List<TimeZoneInfo> TimeZones = [];
        private string SelectedTimeZone = string.Empty;
        private string _seconds = string.Empty;
        private string _newTimeStr = string.Empty;
        private string _newDateStr = string.Empty;

        private void Now()
        {
            var now = DateTime.Now;
            _newDate = DateOnly.FromDateTime(now);
            _newTime = TimeOnly.FromDateTime(now);
            _seconds = now.Second.ToString();
            SelectedTimeZone = TimeZoneInfo.Local.DisplayName;
        }

        private static double GetSeconds(DateTimeOffset dt)
        {
            DateTimeOffset majorPart = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0);
            long secondsTicks = dt.DateTime.Ticks - majorPart.Ticks;
            return (new TimeSpan(secondsTicks)).TotalSeconds;
        }

        private void UtcNow()
        {
            var now = DateTimeOffset.UtcNow;
            _newDate = DateOnly.FromDateTime(now.DateTime);
            _newTime = TimeOnly.FromDateTime(now.DateTime);
            _seconds = now.Second.ToString();
            var utcOffset = new TimeSpan(0, 0, 0);
            SelectedTimeZone = TimeZones.First(tz => tz.BaseUtcOffset == utcOffset).DisplayName;
        }

        protected override void OnParametersSet()
        {
            var editorObject = EditorObject;
            if (editorObject?.Value != null && editorObject.Value.GetType().FullName == typeof(DateTimeOffset).FullName)
            {
                if (PreviousEditorObject is null || PreviousEditorObject != editorObject)
                {
                    TimeZones = TimeZoneInfo.GetSystemTimeZones().ToList();
                    _newDate = DateOnly.FromDateTime(((DateTimeOffset)editorObject.Value).DateTime);
                    _newDateStr = _newDate.ToString();
                    _newTime = TimeOnly.FromDateTime(((DateTimeOffset)editorObject.Value).DateTime);
                    _newTimeStr = _newTime.ToString();
                    _seconds = GetSeconds((DateTimeOffset)editorObject.Value).ToString();
                    SelectedTimeZone = TimeZoneInfo.Local.DisplayName;
                }
                PreviousEditorObject = editorObject;
            }
        }

        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            if (EditorObject?.Value != null)
            {
                var tz = TimeZones.First(tz => tz.DisplayName == SelectedTimeZone).BaseUtcOffset;
                var dateTimeOffset = new DateTimeOffset(_newDate, _newTime, tz);
                dateTimeOffset = new DateTimeOffset(dateTimeOffset.Year, dateTimeOffset.Month, dateTimeOffset.Day, dateTimeOffset.Hour, dateTimeOffset.Minute, 0, tz);

                if (!string.IsNullOrEmpty(_seconds))
                {
                    double secondsDouble = double.Parse(_seconds);
                    dateTimeOffset = dateTimeOffset.AddSeconds(secondsDouble);
                }
                var editorObjectString = JsonUtility.GetJsonStringForObject(dateTimeOffset);
                if (string.IsNullOrEmpty(editorObjectString)) return false;
                if (getName && EditorObject?.Name != null)
                {
                    jsonStringBuilder.Append($"\"{EditorObject.Name}\": ");
                }
                jsonStringBuilder.Append($"{editorObjectString}");
                return true;
            }
            return false;
        }

        private async Task OnValueChanged(ChangeEventArgs e)
        {
            if (IsReadOnly) return;
            var currentValue = _seconds;
            var resetToCurrent = false;
            try
            {
                if (string.IsNullOrEmpty(e.Value?.ToString()))
                {
                    _seconds = string.Empty;
                    return;
                }
                else
                {
                    double convertedValueStr;
                    if (!double.TryParse(e.Value?.ToString(), out convertedValueStr) || convertedValueStr >= 60.00)
                    {
                        _seconds = string.Empty;
                        resetToCurrent = true;
                    }
                    else
                    {
                        _seconds = e.Value?.ToString()!;
                    }
                }
            }
            catch
            {
                _seconds = e.Value?.ToString() ?? string.Empty;
                resetToCurrent = true;
            }
            if (resetToCurrent)
            {
                StateHasChanged();
                await Task.Delay(1);
                _seconds = currentValue ?? string.Empty;
                StateHasChanged();
            }
        }

    }
}
