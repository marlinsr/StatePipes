using Npgsql;
using StatePipes.Workflow.Internal;
using StatePipes.Workflow.Internal.Postgres;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class AdvisoryLockKeyTest
    {
        [TestMethod]
        public void IsStableAcrossProcesses() =>
            // First 8 bytes (little-endian) of SHA-256("Samples.CartWorkflow\0cart-1"), computed independently in PowerShell.
            Assert.AreEqual(3179429984159053158L, AdvisoryLockKey.For("Samples.CartWorkflow", "cart-1"));

        [TestMethod]
        public void SeparatorPreventsConcatenationCollisions() =>
            Assert.AreNotEqual(AdvisoryLockKey.For("a", "bc"), AdvisoryLockKey.For("ab", "c"));
    }

    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class PostgresConnectionIntegrationTest
    {
        [TestMethod]
        public void ConnectsWithClientCert_VerifyFull()
        {
            using var ds = PostgresConnectionFactory.Create(PostgresTestConfig.RequireOrInconclusive());
            using var cmd = ds.CreateCommand("select current_user");
            Assert.AreEqual("statepipes", cmd.ExecuteScalar());
        }

        [TestMethod]
        public void PasswordLoginRefused()
        {
            var cfg = PostgresTestConfig.RequireOrInconclusive();
            var cs = $"Host={cfg.Host};Port={cfg.Port};Database={cfg.Database};Username=postgres;Password=StatePipes123$;SSL Mode=Require;Trust Server Certificate=true";
            using var conn = new NpgsqlConnection(cs);
            Assert.ThrowsExactly<PostgresException>(() => conn.Open());
        }

        [TestMethod]
        public void NonTlsRefused()
        {
            var cfg = PostgresTestConfig.RequireOrInconclusive();
            var cs = $"Host={cfg.Host};Port={cfg.Port};Database={cfg.Database};Username=statepipes;SSL Mode=Disable";
            using var conn = new NpgsqlConnection(cs);
            Assert.ThrowsExactly<PostgresException>(() => conn.Open());
        }

        [TestMethod]
        public void EnsureSchema_IsIdempotent()
        {
            using var ds = PostgresConnectionFactory.Create(PostgresTestConfig.RequireOrInconclusive());
            WorkflowSchema.Ensure(ds);
            WorkflowSchema.Ensure(ds);
            using var cmd = ds.CreateCommand("select count(*) from information_schema.tables where table_schema = 'statepipes' and table_name in ('workflow_instance','workflow_timer')");
            Assert.AreEqual(2L, cmd.ExecuteScalar());
        }
    }
}
