using Microsoft.AspNetCore.Components;
using StatePipes.Common;
using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class DateTimeViewer
    {
        private PropertyValueClass? PreviousEditorObject;
        private DateOnly _newDate = new DateOnly();
        private TimeOnly _newTime = new TimeOnly();
        private string _seconds = string.Empty;
        private string _newTimeStr = string.Empty;
        private string _newDateStr = string.Empty;

        private void Now()
        {
            var now = DateTime.Now;
            _newDate = DateOnly.FromDateTime(now);
            _newTime = TimeOnly.FromDateTime(now);
            _seconds = now.Second.ToString();
        }

        private static double GetSeconds(DateTime dt)
        {
            DateTime majorPart = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0);
            long secondsTicks = dt.Ticks - majorPart.Ticks;
            return (new TimeSpan(secondsTicks)).TotalSeconds;
        }

        protected override void OnParametersSet()
        {
            var editorObject = EditorObject;
            if (editorObject?.Value != null && editorObject.Value.GetType().FullName == typeof(DateTime).FullName)
            {
                if (PreviousEditorObject is null || PreviousEditorObject != editorObject)
                {
                    _newDate = DateOnly.FromDateTime((DateTime)editorObject.Value);
                    _newDateStr = _newDate.ToString();
                    _newTime = TimeOnly.FromDateTime((DateTime)editorObject.Value);
                    _newTimeStr = _newTime.ToString();
                    _seconds = GetSeconds((DateTime)editorObject.Value).ToString();
                }
                PreviousEditorObject = editorObject;
            }
        }

        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            if (EditorObject?.Value != null)
            {
                var dateTime = new DateTime(_newDate, _newTime);
                dateTime = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, 0);

                if (!string.IsNullOrEmpty(_seconds))
                {
                    double secondsDouble = double.Parse(_seconds);
                    dateTime = dateTime.AddSeconds(secondsDouble);
                }
                var editorObjectString = JsonUtility.GetJsonStringForObject(dateTime);
                if (EditorObject == null || string.IsNullOrEmpty(editorObjectString)) return false;
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
