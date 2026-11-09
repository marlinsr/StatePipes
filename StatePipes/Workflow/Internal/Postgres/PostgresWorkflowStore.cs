using Npgsql;
using NpgsqlTypes;
using StatePipes.Comms;

namespace StatePipes.Workflow.Internal.Postgres
{
    /// <summary>
    /// The production workflow store (spec §5). Every unit of work is one PostgreSQL transaction; the instance lock is
    /// pg_try_advisory_xact_lock, so it is released on commit, rollback or a dropped connection. Synchronous Npgsql API:
    /// this runs on the service's bus thread (CLAUDE.md rule 1 forbids blocking on async).
    /// </summary>
    internal sealed class PostgresWorkflowStore(WorkflowStoreConfig config) : IWorkflowStore, IDisposable
    {
        private readonly NpgsqlDataSource _dataSource = PostgresConnectionFactory.Create(config);

        public void EnsureSchema() => WorkflowSchema.Ensure(_dataSource);

        public IWorkflowUnitOfWork Begin()
        {
            var connection = _dataSource.OpenConnection();
            try { return new UnitOfWork(connection, connection.BeginTransaction()); }
            catch { connection.Dispose(); throw; }
        }

        public int FireDueTimers(IReadOnlyCollection<string> workflowTypes, int batchSize, Func<TimerRecord, bool> send, IReadOnlyCollection<Guid>? excluded = null)
        {
            using var connection = _dataSource.OpenConnection();
            using var transaction = connection.BeginTransaction();
            List<TimerRecord> due = [];
            using (var select = Command(connection, transaction, """
                SELECT timer_id, workflow_type, correlation_id, period, message_type_full_name, message_json::text
                FROM statepipes.workflow_timer
                WHERE workflow_type = ANY(@types) AND expiration_time <= now() AND NOT (timer_id = ANY(@excluded))
                ORDER BY expiration_time
                LIMIT @batch
                FOR UPDATE SKIP LOCKED
                """))
            {
                select.Parameters.AddWithValue("types", workflowTypes.ToArray());
                select.Parameters.AddWithValue("batch", batchSize);
                select.Parameters.AddWithValue("excluded", (excluded ?? []).ToArray());
                using var reader = select.ExecuteReader();
                while (reader.Read())
                    due.Add(new TimerRecord(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetFieldValue<TimeSpan>(3), reader.GetString(4), reader.GetString(5)));
            }
            int fired = 0;
            foreach (var timer in due)
            {
                if (!send(timer)) continue;
                fired++;
                // Skip-missed (D16): the next due time on the original cadence strictly after now (see PeriodicSchedule).
                using var after = Command(connection, transaction, timer.Period == null
                    ? "DELETE FROM statepipes.workflow_timer WHERE timer_id = @id"
                    : """
                      UPDATE statepipes.workflow_timer
                      SET expiration_time = expiration_time + period *
                          GREATEST(1, FLOOR(EXTRACT(EPOCH FROM (now() - expiration_time)) / EXTRACT(EPOCH FROM period)) + 1)
                      WHERE timer_id = @id
                      """);
                after.Parameters.AddWithValue("id", timer.TimerId);
                after.ExecuteNonQuery();
            }
            transaction.Commit();
            return fired;
        }

        public void Dispose() => _dataSource.Dispose();

        private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql) => new(sql, connection, transaction);

        private sealed class UnitOfWork(NpgsqlConnection connection, NpgsqlTransaction transaction) : IWorkflowUnitOfWork
        {
            private NpgsqlCommand Command(string sql) => PostgresWorkflowStore.Command(connection, transaction, sql);

            public bool TryLock(string workflowType, string correlationId)
            {
                using var cmd = Command("SELECT pg_try_advisory_xact_lock(@key)");
                cmd.Parameters.AddWithValue("key", AdvisoryLockKey.For(workflowType, correlationId));
                return (bool)cmd.ExecuteScalar()!;
            }

            public WorkflowRecord? Load(string workflowType, string correlationId)
            {
                using var cmd = Command("""
                    SELECT current_state, document::text, version FROM statepipes.workflow_instance
                    WHERE workflow_type = @type AND correlation_id = @id
                    """);
                cmd.Parameters.AddWithValue("type", workflowType);
                cmd.Parameters.AddWithValue("id", correlationId);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? new WorkflowRecord(workflowType, correlationId, reader.GetString(0), reader.GetString(1), reader.GetInt64(2)) : null;
            }

            public void Upsert(string workflowType, string correlationId, string currentState, string documentJson)
            {
                using var cmd = Command("""
                    INSERT INTO statepipes.workflow_instance (workflow_type, correlation_id, current_state, document, version, created_at, updated_at)
                    VALUES (@type, @id, @state, @document::jsonb, 1, now(), now())
                    ON CONFLICT (workflow_type, correlation_id) DO UPDATE
                    SET current_state = EXCLUDED.current_state, document = EXCLUDED.document,
                        version = statepipes.workflow_instance.version + 1, updated_at = now()
                    """);
                cmd.Parameters.AddWithValue("type", workflowType);
                cmd.Parameters.AddWithValue("id", correlationId);
                cmd.Parameters.AddWithValue("state", currentState);
                cmd.Parameters.AddWithValue("document", documentJson);
                cmd.ExecuteNonQuery();
            }

            public void DeleteInstanceAndTimers(string workflowType, string correlationId)
            {
                using var cmd = Command("""
                    DELETE FROM statepipes.workflow_instance WHERE workflow_type = @type AND correlation_id = @id;
                    DELETE FROM statepipes.workflow_timer WHERE workflow_type = @type AND correlation_id = @id;
                    """);
                cmd.Parameters.AddWithValue("type", workflowType);
                cmd.Parameters.AddWithValue("id", correlationId);
                cmd.ExecuteNonQuery();
            }

            public void UpsertTimer(TimerRecord timer, TimeSpan dueTime)
            {
                using var cmd = Command("""
                    INSERT INTO statepipes.workflow_timer (timer_id, workflow_type, correlation_id, expiration_time, period, message_type_full_name, message_json)
                    VALUES (@timer, @type, @id, now() + @due, @period, @messageType, @message::jsonb)
                    ON CONFLICT (timer_id) DO UPDATE
                    SET workflow_type = EXCLUDED.workflow_type, correlation_id = EXCLUDED.correlation_id,
                        expiration_time = EXCLUDED.expiration_time, period = EXCLUDED.period,
                        message_type_full_name = EXCLUDED.message_type_full_name, message_json = EXCLUDED.message_json
                    """);
                cmd.Parameters.AddWithValue("timer", timer.TimerId);
                cmd.Parameters.AddWithValue("type", timer.WorkflowType);
                cmd.Parameters.AddWithValue("id", timer.CorrelationId);
                cmd.Parameters.AddWithValue("due", NpgsqlDbType.Interval, dueTime);
                cmd.Parameters.AddWithValue("period", NpgsqlDbType.Interval, (object?)timer.Period ?? DBNull.Value);
                cmd.Parameters.AddWithValue("messageType", timer.MessageTypeFullName);
                cmd.Parameters.AddWithValue("message", timer.MessageJson);
                cmd.ExecuteNonQuery();
            }

            public void DeleteTimer(Guid timerId)
            {
                using var cmd = Command("DELETE FROM statepipes.workflow_timer WHERE timer_id = @timer");
                cmd.Parameters.AddWithValue("timer", timerId);
                cmd.ExecuteNonQuery();
            }

            public bool TimerExists(Guid timerId)
            {
                using var cmd = Command("SELECT EXISTS (SELECT 1 FROM statepipes.workflow_timer WHERE timer_id = @timer)");
                cmd.Parameters.AddWithValue("timer", timerId);
                return (bool)cmd.ExecuteScalar()!;
            }

            public void Commit() => transaction.Commit();

            public void Dispose()
            {
                transaction.Dispose();
                connection.Dispose();
            }
        }
    }
}
