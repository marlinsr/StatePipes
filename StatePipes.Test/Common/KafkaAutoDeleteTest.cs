using Confluent.Kafka;
using StatePipes.Comms.Internal;

namespace StatePipes.Test.Common
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class KafkaAutoDeleteTest
    {
        private const string Topic = "svc.0123456789abcdef0123456789abcdef.responses";
        private static string Group(string topic) => $"{topic}.{Guid.NewGuid():N}";

        [TestMethod]
        public void HasLiveConsumer_NoGroups_False() =>
            Assert.IsFalse(KafkaAutoDelete.HasLiveConsumer([], Topic, null));

        [TestMethod]
        public void HasLiveConsumer_OnlyEmptyAndDeadGroups_False() =>
            Assert.IsFalse(KafkaAutoDelete.HasLiveConsumer([(Group(Topic), ConsumerGroupState.Empty), (Group(Topic), ConsumerGroupState.Dead)], Topic, null));

        [TestMethod]
        public void HasLiveConsumer_StableGroup_True() =>
            Assert.IsTrue(KafkaAutoDelete.HasLiveConsumer([(Group(Topic), ConsumerGroupState.Stable)], Topic, null));

        [TestMethod]
        public void HasLiveConsumer_RebalancingGroup_True() =>
            Assert.IsTrue(KafkaAutoDelete.HasLiveConsumer([(Group(Topic), ConsumerGroupState.PreparingRebalance)], Topic, null));

        [TestMethod]
        public void HasLiveConsumer_OnlyExcludedGroup_False()
        {
            var own = Group(Topic);
            Assert.IsFalse(KafkaAutoDelete.HasLiveConsumer([(own, ConsumerGroupState.Stable)], Topic, own));
        }

        [TestMethod]
        public void HasLiveConsumer_GroupsOfTopicsSharingThePrefix_False() =>
            Assert.IsFalse(KafkaAutoDelete.HasLiveConsumer(
                [(Group($"{Topic}.other"), ConsumerGroupState.Stable), (Group($"{Topic}X"), ConsumerGroupState.Stable), ($"{Topic}.not-a-guid", ConsumerGroupState.Stable)],
                Topic, null));

        [TestMethod]
        public void Tracker_DrainReturnsTrackedTopicsOnce()
        {
            var tracker = new KafkaAutoDeleteTopics();
            tracker.Track("a.responses", "a.responses.g");
            tracker.Track("b.responses", "b.responses.g");
            CollectionAssert.AreEquivalent(new[] { ("a.responses", "a.responses.g"), ("b.responses", "b.responses.g") }, tracker.Drain().ToArray());
            Assert.AreEqual(0, tracker.Drain().Count);
        }
    }
}
