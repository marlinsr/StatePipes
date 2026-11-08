using Moq;
using Newtonsoft.Json;
using StatePipes.Common.Internal;

namespace StatePipes.Test.Common
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class DelayedMessageSenderTest
    {
        [TestMethod]
        public void Delegates_ToTimerService()
        {
            var timers = new Mock<ITimerService>();
            var id = Guid.NewGuid();
            var sender = new DelayedMessageSender<Ping>(timers.Object, id);
            var ping = new Ping(7);
            timers.Setup(t => t.IsEnabled(id)).Returns(true);

            sender.StartOneShot(TimeSpan.FromSeconds(5), ping);
            sender.StartPeriodic(TimeSpan.FromSeconds(2), ping);

            timers.Verify(t => t.StartOneShot(id, TimeSpan.FromSeconds(5), ping), Times.Once);
            timers.Verify(t => t.StartPeriodic(id, TimeSpan.FromSeconds(2), ping), Times.Once);
            Assert.AreEqual(id, sender.TimerId);
            Assert.IsTrue(sender.Enabled);
            sender.Dispose();
            timers.Verify(t => t.Stop(id), Times.Once);
        }

        [TestMethod]
        public void Enabled_IsNotSerialized()
        {
            var sender = new DelayedMessageSender<Ping>(Mock.Of<ITimerService>(), Guid.NewGuid());
            var json = JsonConvert.SerializeObject(sender);
            StringAssert.Contains(json, "\"TimerId\"");
            Assert.IsFalse(json.Contains("Enabled"), json);
        }
    }
}
