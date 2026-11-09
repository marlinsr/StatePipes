using Npgsql;

namespace StatePipes.Workflow.Internal.Postgres
{
    /// <summary>The framework-owned workflow tables (spec §5.1), created idempotently at startup.</summary>
    internal static class WorkflowSchema
    {
        public const string Ddl = """
            CREATE SCHEMA IF NOT EXISTS statepipes;

            CREATE TABLE IF NOT EXISTS statepipes.workflow_instance (
              workflow_type   text        NOT NULL,
              correlation_id  text        NOT NULL,
              current_state   text        NOT NULL,
              document        jsonb       NOT NULL,
              version         bigint      NOT NULL,
              created_at      timestamptz NOT NULL,
              updated_at      timestamptz NOT NULL,
              PRIMARY KEY (workflow_type, correlation_id));

            CREATE TABLE IF NOT EXISTS statepipes.workflow_timer (
              timer_id               uuid        PRIMARY KEY,
              workflow_type          text        NOT NULL,
              correlation_id         text        NOT NULL,
              expiration_time        timestamptz NOT NULL,
              period                 interval    NULL,
              message_type_full_name text        NOT NULL,
              message_json           jsonb       NOT NULL);

            CREATE INDEX IF NOT EXISTS workflow_timer_expiration_idx ON statepipes.workflow_timer (expiration_time);
            CREATE INDEX IF NOT EXISTS workflow_timer_owner_idx      ON statepipes.workflow_timer (workflow_type, correlation_id);
            """;

        public static void Ensure(NpgsqlDataSource dataSource)
        {
            using var command = dataSource.CreateCommand(Ddl);
            command.ExecuteNonQuery();
        }
    }
}
