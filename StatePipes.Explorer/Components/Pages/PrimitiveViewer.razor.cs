using Microsoft.AspNetCore.Components;
using System.Text;
namespace StatePipes.Explorer.Components.Pages
{
    public partial class PrimitiveViewer
    {
        private string _objectVal = string.Empty;
        private PropertyValueClass? _previousEditorObject;
        private bool _nullable;
        private IPrimitiveWorker? _primitiveWorker;
        private string _typeName = string.Empty;

        protected override void OnParametersSet()
        {
            if (_previousEditorObject != EditorObject)
            {
                _nullable = EditorObject?.Nullable ?? false;
                SetupObject<uint>(false);
                SetupObject<long>(true);
                SetupObject<ulong>(false);
                SetupObject<sbyte>(true);
                SetupObject<short>(true);
                SetupObject<int>(true);
                SetupObject<char>(false);
                SetupObject<byte>(false);
                SetupObject<ushort>(false);
                SetupObject<double>(false);
                SetupObject<float>(false);
            }
            _previousEditorObject = EditorObject;
        }

        private void SetupObject<T>(bool signed)
        {
            if (EditorObject?.PropertyType?.FullName != typeof(T).FullName) return;
            _typeName = typeof(T).Name;
            _primitiveWorker = new PrimitiveWorker<T>();
            if (_nullable && string.IsNullOrEmpty(EditorObject?.Value?.ToString()))
            {
                _objectVal = string.Empty;
            }
            else
            {
                var valStr = _primitiveWorker.GetValueFromString(EditorObject?.Value?.ToString()!, _nullable);
                if (valStr != null) _objectVal = valStr;
            }
        }

        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            if (getName && EditorObject?.Name != null)
            {
                jsonStringBuilder.Append($"\"{EditorObject.Name}\": ");
            }

            if (!string.IsNullOrEmpty(_objectVal))
            {
                jsonStringBuilder.Append(_objectVal);
                return true;
            }
            else if (!_nullable)
            {
                jsonStringBuilder.Append(_primitiveWorker?.DefaultValue());
                return true;
            }
            return false;
        }

        private async Task OnValueChanged(ChangeEventArgs e)
        {
            var currentValue = _objectVal;
            var resetToCurrent = false;
            try
            {
                if (string.IsNullOrEmpty(e.Value?.ToString()))
                {
                    _objectVal = string.Empty;
                    return;
                }
                else
                {
                    var convertedValueStr = _primitiveWorker?.GetValueFromString(e.Value?.ToString(), _nullable);
                    if (convertedValueStr == null)
                    {
                        _objectVal = string.Empty;
                        resetToCurrent = true;
                    }
                    else
                    {
                        _objectVal = convertedValueStr;
                    }
                }
            }
            catch
            {
                _objectVal = e.Value?.ToString() ?? string.Empty;
                resetToCurrent = true;
            }
            if (resetToCurrent)
            {
                StateHasChanged();
                await Task.Delay(1);
                _objectVal = currentValue;
                StateHasChanged();
            }
        }

    }
}
