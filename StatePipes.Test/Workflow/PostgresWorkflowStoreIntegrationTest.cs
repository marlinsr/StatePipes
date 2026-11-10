using Npgsql;
using StatePipes.Workflow.Internal;
using StatePipes.Workflow.Internal.Postgres;
using System.Collections.Concurrent;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class PostgresWorkflowStoreIntegrationTest
    {
        private NpgsqlDataSource _ds = null!;
        private PostgresWorkflowStore _store = null!;
        private readonly string _type = $"it.{Guid.NewGuid():N}";

        [TestInitialize]
        public void Init()
        {
            var cfg = PostgresTestConfig.RequireOrInconclusive();
            _ds = PostgresConnectionFactory.Create(cfg);
            _store = new PostgresWorkflowStore(cfg);
            _store.EnsureSchema();
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (_ds == null) return;
            if (Environment.GetEnvironmentVariable("STATEPIPES_KEEP_IT_ROWS") != "1")
            {
                Exec("delete from statepipes.workflow_instance where workflow_type = @t");
                Exec("delete from statepipes.workflow_timer where workflow_type = @t");
            }
            _store.Dispose();
            _ds.Dispose();
        }

        private object? Exec(string sql, Guid? timerId = null)
        {
            using var cmd = _ds.CreateCommand(sql);
            cmd.Parameters.AddWithValue("t", _type);
            if (timerId is Guid id) cmd.Parameters.AddWithValue("id", id);
            return cmd.ExecuteScalar();
        }

        private TimerRecord Timer(string cid, TimeSpan? period = null) => new(Guid.NewGuid(), _type, cid, period, "Some.Message", "{\"a\":1}");

        [TestMethod]
        public void RoundTrip_UpsertLoad_VersionIncrements()
        {
            for (int i = 1; i <= 2; i++)
            {
                using var uow = _store.Begin();
                uow.Upsert(_type, "c1", $"S{i}", "{\"states\":{\"S\":{\"n\":" + i + "}}}");
                uow.Commit();
            }
            using var read = _store.Begin();
            var rec = read.Load(_type, "c1")!;
            Assert.AreEqual(("S2", 2L), (rec.CurrentState, rec.Version));
            StringAssert.Contains(rec.DocumentJson, "\"n\": 2");
        }

        [TestMethod]
        public void Rollback_LeavesNoRow()
        {
            using (var uow = _store.Begin())
            {
                uow.Upsert(_type, "c1", "S", "{}");
                uow.UpsertTimer(Timer("c1"), TimeSpan.FromMinutes(1));
            }
            Assert.AreEqual(0L, Exec("select count(*) from statepipes.workflow_instance where workflow_type = @t"));
            Assert.AreEqual(0L, Exec("select count(*) from statepipes.workflow_timer where workflow_type = @t"));
        }

        [TestMethod]
        public void AdvisoryLock_ContendedAcrossTwoUnits()
        {
            var a = _store.Begin();
            Assert.IsTrue(a.TryLock(_type, "c1"));
            using var b = _store.Begin();
            Assert.IsFalse(b.TryLock(_type, "c1"));
            Assert.IsTrue(b.TryLock(_type, "c2"));
            a.Commit();
            Assert.IsTrue(b.TryLock(_type, "c1"));
        }

        [TestMethod]
        public void AdvisoryLock_WorksBeforeRowExists()
        {
            using var a = _store.Begin();
            Assert.IsNull(a.Load(_type, "new"));
            Assert.IsTrue(a.TryLock(_type, "new"));
            using var b = _store.Begin();
            Assert.IsFalse(b.TryLock(_type, "new"));
        }

        [TestMethod]
        public void DeleteInstanceAndTimers_RemovesBoth()
        {
            using (var uow = _store.Begin())
            {
                uow.Upsert(_type, "c1", "S", "{}");
                uow.UpsertTimer(Timer("c1"), TimeSpan.FromDays(1));
                uow.UpsertTimer(Timer("c2"), TimeSpan.FromDays(1));
                uow.Commit();
            }
            using (var uow = _store.Begin())
            {
                Assert.IsTrue(uow.TimerExists(Guid.Empty) == false);
                uow.DeleteInstanceAndTimers(_type, "c1");
                uow.Commit();
            }
            Assert.AreEqual(0L, Exec("select count(*) from statepipes.workflow_instance where workflow_type = @t"));
            Assert.AreEqual(1L, Exec("select count(*) from statepipes.workflow_timer where workflow_type = @t"));
        }

        [TestMethod]
        public void FireDueTimers_TwoConcurrentPollers_EachTimerOnce()
        {
            using (var uow = _store.Begin())
            {
                for (int i = 0; i < 50; i++) uow.UpsertTimer(Timer($"c{i}"), TimeSpan.Zero);
                uow.Commit();
            }
            var sent = new ConcurrentBag<Guid>();
            using var other = new PostgresWorkflowStore(PostgresTestConfig.RequireOrInconclusive());
            var pollers = new[] { _store, other }.Select(s => Task.Run(() =>
            {
                while (s.FireDueTimers([_type], 7, t => { sent.Add(t.TimerId); Thread.Sleep(2); return true; }) > 0) { }
            })).ToArray();
            Task.WaitAll(pollers);
            Assert.AreEqual(50, sent.Count);
            Assert.AreEqual(50, sent.Distinct().Count());
        }

        [TestMethod]
        public void FireDueTimers_PeriodicOverdue_SkipsMissed()
        {
            var t = Timer("c1", TimeSpan.FromSeconds(1));
            using (var uow = _store.Begin())
            {
                uow.UpsertTimer(t, TimeSpan.Zero);
                uow.Commit();
            }
            Exec("update statepipes.workflow_timer set expiration_time = now() - interval '10.5 seconds' where timer_id = @id", t.TimerId);
            int sends = 0;
            Assert.AreEqual(1, _store.FireDueTimers([_type], 100, _ => { sends++; return true; }));
            Assert.AreEqual(1, sends);
            Assert.AreEqual(true, Exec("select expiration_time > now() and expiration_time <= now() + interval '1 second' from statepipes.workflow_timer where timer_id = @id", t.TimerId));
        }

        [TestMethod]
        public void FireDueTimers_SendFalse_LeavesRow()
        {
            var t = Timer("c1");
            using (var uow = _store.Begin())
            {
                uow.UpsertTimer(t, TimeSpan.Zero);
                uow.Commit();
            }
            Assert.AreEqual(0, _store.FireDueTimers([_type], 100, _ => false));
            Assert.AreEqual(1L, Exec("select count(*) from statepipes.workflow_timer where timer_id = @id", t.TimerId));
        }
    }
}
