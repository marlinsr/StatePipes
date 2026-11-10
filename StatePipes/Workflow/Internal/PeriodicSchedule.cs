namespace StatePipes.Workflow.Internal
{
    internal static class PeriodicSchedule
    {
        /// <summary>The next due time on the original cadence, strictly after <paramref name="now"/> and at least one period after <paramref name="expiration"/>;
        /// periods missed while nothing was polling are skipped, not replayed (spec D16).</summary>
        public static DateTimeOffset NextDue(DateTimeOffset expiration, TimeSpan period, DateTimeOffset now)
        {
            var periods = Math.Max(1L, (long)Math.Floor((now - expiration).Ticks / (double)period.Ticks) + 1);
            return expiration + TimeSpan.FromTicks(period.Ticks * periods);
        }
    }
}
