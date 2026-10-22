using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Opc.Ua;
using System.Text;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Turns an Exec command's body into the input arguments of its OPC UA Call, one Variant per input in argument
    /// order. Each input is encoded exactly as a Set encodes a value, so it reaches the server as the argument's
    /// own built-in type or not at all.
    /// </summary>
    internal static class ExecArgumentEncoder
    {
        private static readonly JsonSerializerSettings BodySettings = new() { DateParseHandling = DateParseHandling.None };

        /// <summary>
        /// Good with the inputs, or why the body cannot be called with: BadArgumentsMissing when it is not a JSON
        /// object or lacks an input, BadTooManyArguments when it holds a property that is no input, else the first
        /// input's encoding failure (BadTypeMismatch, BadOutOfRange). An empty body is an empty object, for methods
        /// without inputs.
        /// </summary>
        public static StatusCode TryEncode(byte[] body, IReadOnlyList<MethodArgument> inputs, out IReadOnlyList<Variant> values)
        {
            values = [];
            var text = Encoding.UTF8.GetString(body);
            JObject? arguments;
            if (string.IsNullOrWhiteSpace(text)) arguments = [];
            else
            {
                try { arguments = JsonConvert.DeserializeObject<JToken>(text, BodySettings) as JObject; }
                catch (JsonException) { return StatusCodes.BadArgumentsMissing; }
            }
            if (arguments == null || inputs.Any(input => !arguments.ContainsKey(input.PropertyName))) return StatusCodes.BadArgumentsMissing;
            if (arguments.Count > inputs.Count) return StatusCodes.BadTooManyArguments;
            List<Variant> encoded = [];
            foreach (var input in inputs)
            {
                var token = arguments[input.PropertyName];
                var status = input.Structure != null
                    ? StructureEncoder.TryEncode(token, input.Structure, input.IsArray, out var value)
                    : OpcUaValueEncoder.TryEncode(token, input.BuiltInType, input.IsArray, out value);
                if (StatusCode.IsBad(status)) return status;
                encoded.Add(value);
            }
            values = encoded;
            return StatusCodes.Good;
        }
    }
}
