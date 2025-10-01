using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace StatePipes.Explorer.Components.Pages
{
    public class PrimitiveWorker<T> : IPrimitiveWorker
    {
        public string? DefaultValue()
        {
            return default(T)?.ToString();
        }
        public string? GetValueFromString(string? str, bool nullable)
        {
            if (nullable && string.IsNullOrEmpty(str))
            {
                return null;
            }
            else
            {
                var valStr = string.IsNullOrEmpty(str) ? default(T)?.ToString()! : str!;
                return !BindConverter.TryConvertTo<T>(valStr, CultureInfo.InvariantCulture, out var value) ? null : value?.ToString();
            }
        }

    }
}
