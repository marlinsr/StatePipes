using Confluent.Kafka;
using Confluent.Kafka.Admin;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.Comms.Internal
{
    /// <summary>
    /// Kafka stand-in for RabbitMQ's auto-delete exchanges. Kafka has nothing tied to a connection, so a topic
    /// counts as in use while a consumer group reading it is alive. Both Kafka transports name their groups
    /// <c>{topic}.{guid:N}</c>, which is how a topic's groups are found.
    /// </summary>
    internal static class KafkaAutoDelete
    {
        private static readonly TimeSpan AdminTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// True when a group of <paramref name="topic"/>, other than <paramref name="excludeGroupId"/>, is not Empty or Dead.
        /// An Unknown state counts as live: unknown is not missing.
        /// </summary>
        public static bool HasLiveConsumer(IEnumerable<(string GroupId, ConsumerGroupState State)> groups, string topic, string? excludeGroupId) =>
            groups.Any(g => g.GroupId != excludeGroupId && IsGroupOf(g.GroupId, topic) && g.State is not (ConsumerGroupState.Empty or ConsumerGroupState.Dead));

        /// <summary>Lists the cluster's consumer groups; throws when the broker cannot be asked.</summary>
        public static async Task<bool> HasLiveConsumerAsync(IAdminClient admin, string topic, string? excludeGroupId)
        {
            var result = await admin.ListConsumerGroupsAsync(new ListConsumerGroupsOptions { RequestTimeout = AdminTimeout });
            return HasLiveConsumer(result.Valid.Select(g => (g.GroupId, g.State)), topic, excludeGroupId);
        }

        /// <summary>
        /// Deletes <paramref name="topic"/> unless a group other than <paramref name="ownGroupId"/> still reads it.
        /// Never throws: a failed check or delete is logged and the topic is left in place.
        /// </summary>
        public static async Task DeleteIfUnusedAsync(IAdminClient admin, string topic, string ownGroupId)
        {
            try
            {
                if (await HasLiveConsumerAsync(admin, topic, ownGroupId)) return;
                await admin.DeleteTopicsAsync([topic], new DeleteTopicsOptions { RequestTimeout = AdminTimeout, OperationTimeout = AdminTimeout });
            }
            catch (DeleteTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.UnknownTopicOrPart)) { /* already gone */ }
            catch (Exception ex) { Log?.LogVerbose($"Could not auto-delete Kafka topic {topic}: {ex.Message}"); }
        }

        private static bool IsGroupOf(string groupId, string topic) =>
            groupId.Length == topic.Length + 33 && groupId.StartsWith(topic, StringComparison.Ordinal) && groupId[topic.Length] == '.' &&
            Guid.TryParseExact(groupId.AsSpan(topic.Length + 1), "N", out _);
    }

    /// <summary>The auto-delete topics a transport consumes, with its own group on each, for deletion on dispose.</summary>
    internal sealed class KafkaAutoDeleteTopics
    {
        private readonly Lock _lock = new();
        private readonly List<(string Topic, string GroupId)> _topics = [];

        public void Track(string topic, string groupId)
        {
            lock (_lock) _topics.Add((topic, groupId));
        }

        /// <summary>Returns the tracked topics and forgets them, so a second dispose deletes nothing.</summary>
        public IReadOnlyList<(string Topic, string GroupId)> Drain()
        {
            lock (_lock)
            {
                var drained = _topics.ToList();
                _topics.Clear();
                return drained;
            }
        }
    }
}
