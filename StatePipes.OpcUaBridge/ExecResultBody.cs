using Newtonsoft.Json.Linq;
using Opc.Ua;
using System.Text;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Builds the body of an ExecResult event: the object and method called, the status, and, when the call did not
    /// fail, one property per output argument. Outputs are published as Get values are, and one with no value is
    /// left out rather than sent as null, for the same reason a Get event leaves out a missing value.
    /// </summary>
    internal static class ExecResultBody
    {
        public static byte[] Create(OpcUaMethodItem method, CallMethodResult result)
        {
            var body = CreateEnvelope(method, result.StatusCode, StatusText(method, result));
            if (StatusCode.IsBad(result.StatusCode)) return ToBytes(body);
            for (var i = 0; i < method.Outputs.Count && i < result.OutputArguments.Count; i++)
            {
                var output = method.Outputs[i];
                var value = OpcUaValueMapper.ToJToken(result.OutputArguments[i], output.ValueType, output.Structure, output.IsArray);
                if (value.Type != JTokenType.Null) body[output.PropertyName] = value;
            }
            return ToBytes(body);
        }

        /// <summary>The answer to an Exec the bridge does not call; <paramref name="method"/> is null when the catalog does not hold it.</summary>
        public static byte[] Refused(OpcUaMethodItem? method, StatusCode status) => ToBytes(CreateEnvelope(method, status, status.ToString()));

        private static JObject CreateEnvelope(OpcUaMethodItem? method, StatusCode status, string statusText) => new()
        {
            [SelfDescriptionBuilder.ObjectIdProperty] = method?.ObjectIdText ?? string.Empty,
            [SelfDescriptionBuilder.MethodIdProperty] = method?.MethodIdText ?? string.Empty,
            [SelfDescriptionBuilder.StatusCodeProperty] = status.Code,
            [SelfDescriptionBuilder.StatusProperty] = statusText
        };

        /// <summary>The status as text; for BadInvalidArgument, followed by each input the server rejected and why.</summary>
        private static string StatusText(OpcUaMethodItem method, CallMethodResult result)
        {
            var text = result.StatusCode.ToString();
            if (result.StatusCode.Code != StatusCodes.BadInvalidArgument.Code) return text;
            var rejected = Enumerable.Range(0, Math.Min(method.Inputs.Count, result.InputArgumentResults.Count))
                .Where(i => StatusCode.IsBad(result.InputArgumentResults[i]))
                // ToString, not interpolation: a formatted StatusCode carries its hex code, unlike every other Status text.
                .Select(i => $"{method.Inputs[i].PropertyName} ({result.InputArgumentResults[i].ToString()})")
                .ToList();
            return rejected.Count == 0 ? text : $"{text}: {string.Join(", ", rejected)}";
        }

        private static byte[] ToBytes(JObject body) => Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None));
    }
}
