using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StatePipes.Common;
using StatePipes.Messages;

namespace StatePipes.Explorer.NonWebClasses
{
    /// <summary>Pure logic behind the WORKFLOW tab, kept out of the Razor component so it can be unit tested.</summary>
    internal static class WorkflowInterrogation
    {
        public static string? NormalizeInput(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

        public static WorkflowDiagramsEvent? ToEvent(EventEntry entry) => JsonUtility.CloneToType<WorkflowDiagramsEvent>(entry.Obj);

        /// <summary>
        /// True only for a reply that arrived after the request (a different entry than the one present when it was sent)
        /// and that answers the pending correlationId.
        /// </summary>
        public static bool IsMatchingReply(EventEntry? candidate, EventEntry? snapshot, string pendingCorrelationId)
        {
            if (candidate == null || ReferenceEquals(candidate, snapshot)) return false;
            if (candidate.FullName != typeof(WorkflowDiagramsEvent).FullName) return false;
            return string.Equals(ToEvent(candidate)?.CorrelationId, pendingCorrelationId, StringComparison.Ordinal);
        }

        public static string PrettyJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            try { return JToken.Parse(json).ToString(Formatting.Indented); }
            catch (JsonException) { return json; }
        }

        public static string ShortTypeName(string fullName)
        {
            var i = fullName.LastIndexOf('.');
            return i < 0 ? fullName : fullName[(i + 1)..];
        }

        public static string StatusText(string correlationId, int count) => count switch
        {
            0 => $"No running workflows for '{correlationId}'",
            1 => "1 workflow found",
            _ => $"{count} workflows found"
        };
    }
}
