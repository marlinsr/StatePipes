using Microsoft.Extensions.Time.Testing;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;
using System.Collections.Concurrent;

namespace StatePipes.Test.Common
{
    internal record Ping(int N = 0) : IMessage;

    internal sealed class RecordingSender : IMessageSender
    {
        public readonly ConcurrentQueue<IMessage> Sent = new();
        public Func<IMessage, bool>? ThrowWhen;
        public void SendMessage<TMessage>(TMessage message) where TMessage : class, IMessage
        {
            if (ThrowWhen?.Invoke(message) == true) throw new InvalidOperationException("send failed");
            Sent.Enqueue(message);
        }
    }

    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class InMemoryTimerServiceTest
    {
        private static FakeTimeProvider NewTime() => new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        internal static void WaitFor(Func<bool> condition, int timeoutMs = 1000)
        {
            var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < end) Thread.Sleep(5);
        }
        // Give the timer thread a chance to (wrongly) deliver, then assert count.
        private static void Settle() => Thread.Sleep(100);

        [TestMethod]
        public void OneShot_FiresOnceAtDueTime()
        {
            var time = NewTime(); var sender = new RecordingSender();
            using var timers = new InMemoryTimerService(sender, time);
            var id = Guid.NewGuid();
            timers.StartOneShot(id, TimeSpan.FromSeconds(5), new Ping());
            time.Advance(TimeSpan.FromSeconds(4.9)); Settle();
            Assert.AreEqual(0, sender.Sent.Count);
            time.Advance(TimeSpan.FromSeconds(0.1)); WaitFor(() => sender.Sent.Count == 1);
            Assert.AreEqual(1, sender.Sent.Count);
            time.Advance(TimeSpan.FromSeconds(10)); Settle();
            Assert.AreEqual(1, sender.Sent.Count);
            Assert.IsFalse(timers.IsEnabled(id));
        }

        [TestMethod]
        public void Periodic_FiresEachPeriodWithoutDrift()
        {
            var time = NewTime(); var sender = new RecordingSender();
            using var timers = new InMemoryTimerService(sender, time);
            var id = Guid.NewGuid();
            timers.StartPeriodic(id, TimeSpan.FromSeconds(1), new Ping());
            for (int i = 1; i <= 3; i++)
            {
                time.Advance(TimeSpan.FromSeconds(1));
                WaitFor(() => sender.Sent.Count == i);
            }
            Assert.AreEqual(3, sender.Sent.Count);
            Assert.IsTrue(timers.IsEnabled(id));
        }

        [TestMethod]
        public void Start_SameTimerId_ReplacesPending()
        {
            var time = NewTime(); var sender = new RecordingSender();
            using var timers = new InMemoryTimerService(sender, time);
            var id = Guid.NewGuid();
            timers.StartOneShot(id, TimeSpan.FromSeconds(5), new Ping(1));
            timers.StartOneShot(id, TimeSpan.FromSeconds(10), new Ping(2));
            time.Advance(TimeSpan.FromSeconds(6)); Settle();
            Assert.AreEqual(0, sender.Sent.Count);
            time.Advance(TimeSpan.FromSeconds(4)); WaitFor(() => sender.Sent.Count == 1);
            Assert.AreEqual(1, sender.Sent.Count);
            Assert.AreEqual(new Ping(2), sender.Sent.Single());
        }

        [TestMethod]
        public void Stop_CancelsAndDisables()
        {
            var time = NewTime(); var sender = new RecordingSender();
            using var timers = new InMemoryTimerService(sender, time);
            var id = Guid.NewGuid();
            timers.StartOneShot(id, TimeSpan.FromSeconds(5), new Ping());
            timers.Stop(id);
            time.Advance(TimeSpan.FromSeconds(10)); Settle();
            Assert.AreEqual(0, sender.Sent.Count);
            Assert.IsFalse(timers.IsEnabled(id));
        }

        [TestMethod]
        public void ScheduleCallback_InvokesOnce()
        {
            var time = NewTime(); var sender = new RecordingSender();
            using var timers = new InMemoryTimerService(sender, time);
            int calls = 0;
            timers.ScheduleCallback(TimeSpan.FromMilliseconds(50), () => Interlocked.Increment(ref calls));
            time.Advance(TimeSpan.FromMilliseconds(50)); WaitFor(() => calls == 1);
            time.Advance(TimeSpan.FromSeconds(1)); Settle();
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public void SenderThrows_ThreadSurvives()
        {
            var time = NewTime();
            var sender = new RecordingSender { ThrowWhen = m => m is Ping { N: 1 } };
            using var timers = new InMemoryTimerService(sender, time);
            timers.StartOneShot(Guid.NewGuid(), TimeSpan.FromSeconds(1), new Ping(1));
            timers.StartOneShot(Guid.NewGuid(), TimeSpan.FromSeconds(2), new Ping(2));
            time.Advance(TimeSpan.FromSeconds(1)); Settle();
            time.Advance(TimeSpan.FromSeconds(1)); WaitFor(() => sender.Sent.Count == 1);
            Assert.AreEqual(new Ping(2), sender.Sent.Single());
        }

        /// <summary>Advances the clock at the moment the first wake timer is created, as a concurrent Advance would.</summary>
        private sealed class RacingTimeProvider(FakeTimeProvider inner, TimeSpan advanceBy) : TimeProvider
        {
            private bool _raced;
            public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
            public override long GetTimestamp() => inner.GetTimestamp();
            public override long TimestampFrequency => inner.TimestampFrequency;
            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                if (!_raced) { _raced = true; inner.Advance(advanceBy); }
                return inner.CreateTimer(callback, state, dueTime, period);
            }
        }

        [TestMethod]
        public void ClockAdvancedWhileArmingWakeTimer_StillFires()
        {
            var fake = NewTime();
            var time = new RacingTimeProvider(fake, TimeSpan.FromMilliseconds(50));
            using var timers = new InMemoryTimerService(new RecordingSender(), time);
            Settle(); // timer thread idle, so the schedule signal is consumed before the wake timer is armed
            int calls = 0;
            timers.ScheduleCallback(TimeSpan.FromMilliseconds(50), () => Interlocked.Increment(ref calls));
            WaitFor(() => calls == 1);
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public void ClockPartlyAdvancedWhileArmingWakeTimer_StillFiresWhenDue()
        {
            var fake = NewTime();
            var time = new RacingTimeProvider(fake, TimeSpan.FromMilliseconds(30));
            using var timers = new InMemoryTimerService(new RecordingSender(), time);
            Settle();
            int calls = 0;
            timers.ScheduleCallback(TimeSpan.FromMilliseconds(50), () => Interlocked.Increment(ref calls));
            Settle();
            fake.Advance(TimeSpan.FromMilliseconds(20)); // now exactly due
            WaitFor(() => calls == 1);
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public void Periodic_AfterLongPause_FiresOnceNotOncePerMissedPeriod()
        {
            var time = NewTime(); var sender = new RecordingSender();
            using var timers = new InMemoryTimerService(sender, time);
            timers.StartPeriodic(Guid.NewGuid(), TimeSpan.FromSeconds(1), new Ping());
            time.Advance(TimeSpan.FromSeconds(10));
            WaitFor(() => sender.Sent.Count >= 1); Settle();
            Assert.AreEqual(1, sender.Sent.Count);
            time.Advance(TimeSpan.FromSeconds(1));
            WaitFor(() => sender.Sent.Count == 2);
            Assert.AreEqual(2, sender.Sent.Count);
        }

        /// <summary>Wall clock that can be stepped (NTP, VM resume) while elapsed time keeps running on the fake clock.</summary>
        private sealed class SteppingWallClock(FakeTimeProvider inner) : TimeProvider
        {
            public TimeSpan Step;
            public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() + Step;
            public override long GetTimestamp() => inner.GetTimestamp();
            public override long TimestampFrequency => inner.TimestampFrequency;
            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
                inner.CreateTimer(callback, state, dueTime, period);
        }

        [TestMethod]
        public void WallClockSteppingBack_DoesNotDelayTimers()
        {
            var fake = NewTime(); var sender = new RecordingSender();
            var time = new SteppingWallClock(fake);
            using var timers = new InMemoryTimerService(sender, time);
            timers.StartOneShot(Guid.NewGuid(), TimeSpan.FromSeconds(5), new Ping());
            Settle();
            time.Step = TimeSpan.FromHours(-1);
            fake.Advance(TimeSpan.FromSeconds(5));
            WaitFor(() => sender.Sent.Count == 1);
            Assert.AreEqual(1, sender.Sent.Count);
        }

        [TestMethod]
        public void StartPeriodic_NonPositivePeriod_Throws()
        {
            using var timers = new InMemoryTimerService(new RecordingSender(), NewTime());
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => timers.StartPeriodic(Guid.NewGuid(), TimeSpan.Zero, new Ping()));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => timers.StartPeriodic(Guid.NewGuid(), TimeSpan.FromSeconds(-1), new Ping()));
        }
    }
}
