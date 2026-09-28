using System.Text;

namespace StatePipes.Comms.Internal
{
    /// <summary>
    /// Maps StatePipes exchange names onto Kafka topic names. Under the current
    /// all-ephemeral parity model (see <see cref="KafkaTransport"/>) the mapping is 1:1 with
    /// the RabbitMQ exchange name &mdash; including the per-instance GUID in the response
    /// exchange &mdash; so each transport instance owns its own command/event/response topics
    /// exactly as it owns its own RabbitMQ exchanges/queues today.
    /// Only characters illegal in Kafka topic names (anything outside <c>[a-zA-Z0-9._-]</c>)
    /// are replaced, keeping the names otherwise identical for easy correlation.
    /// </summary>
    internal static class KafkaTopicNamer
    {
        private const int MaxTopicLength = 249;

        public static string ToTopic(string exchangeName)
        {
            if (string.IsNullOrEmpty(exchangeName)) throw new ArgumentException("Exchange name must be non-empty.", nameof(exchangeName));
            var builder = new StringBuilder(exchangeName.Length);
            foreach (var c in exchangeName)
                builder.Append(IsLegal(c) ? c : '_');
            var topic = builder.ToString();
            return topic.Length > MaxTopicLength ? topic[..MaxTopicLength] : topic;
        }

        private static bool IsLegal(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
    }
}
