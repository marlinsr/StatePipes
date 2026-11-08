using StatePipes.Workflow.Internal;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class PeriodicScheduleTest
    {
        private static DateTimeOffset At(string hhmmss) => DateTimeOffset.Parse($"2026-01-01T{hhmmss}Z");

        [TestMethod]
        [DataRow("10:10:15", "10:11:00")]
        [DataRow("09:59:00", "10:01:00")]
        [DataRow("10:03:00", "10:04:00")]
        public void NextDue(string now, string expected) =>
            Assert.AreEqual(At(expected), PeriodicSchedule.NextDue(At("10:00:00"), TimeSpan.FromMinutes(1), At(now)));
    }
}
