using KetoanMini.Api.Data;

namespace KetoanMini.Api.BuildingBlocks.Persistence;

/// <summary>Forward-only, idempotent migration for durable messaging and realtime infrastructure.</summary>
public static class MessagingSchema
{
    public const string Version = "002_pubsub_sse";

    public static async Task EnsureAsync(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        // Nested test hosts and rolling replicas may start concurrently against one database.
        // CREATE INDEX IF NOT EXISTS still takes relation locks, so serialize the one-time DDL and
        // let every later host use the marker fast path instead of repeatedly locking live tables.
        const long migrationLock = 7_641_903_377_002L;
        await conn.Cmd("SELECT pg_advisory_lock(@key)").With("@key", migrationLock).ExecuteNonQueryAsync(ct);
        try
        {
            var installed = await conn.Cmd(
                "SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE version=@version)")
                .With("@version", Version).ExecuteScalarAsync(ct) is true;
            if (!installed) await conn.Cmd(Sql).ExecuteNonQueryAsync(ct);
        }
        finally
        {
            await conn.Cmd("SELECT pg_advisory_unlock(@key)").With("@key", migrationLock)
                .ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    internal const string Sql = """
        CREATE TABLE IF NOT EXISTS integration_outbox (
            id uuid PRIMARY KEY,
            event_type varchar(160) NOT NULL,
            routing_key varchar(200) NOT NULL,
            aggregate_type varchar(100) NULL,
            aggregate_id varchar(160) NULL,
            aggregate_version bigint NULL,
            payload jsonb NOT NULL,
            headers jsonb NOT NULL DEFAULT '{}'::jsonb,
            occurred_at timestamptz NOT NULL,
            available_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            attempts integer NOT NULL DEFAULT 0,
            locked_until timestamptz NULL,
            published_at timestamptz NULL,
            last_error text NOT NULL DEFAULT '',
            bridge_key varchar(200) NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_integration_outbox_bridge
            ON integration_outbox(bridge_key) WHERE bridge_key IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_integration_outbox_ready
            ON integration_outbox(available_at, occurred_at)
            WHERE published_at IS NULL;

        CREATE INDEX IF NOT EXISTS ix_integration_outbox_purge
            ON integration_outbox(published_at) WHERE published_at IS NOT NULL;

        CREATE TABLE IF NOT EXISTS inbox_messages (
            consumer_name varchar(120) NOT NULL,
            message_id uuid NOT NULL,
            received_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            completed_at timestamptz NULL,
            correlation_id varchar(160) NULL,
            PRIMARY KEY (consumer_name, message_id)
        );
        CREATE INDEX IF NOT EXISTS ix_inbox_messages_purge
            ON inbox_messages(completed_at) WHERE completed_at IS NOT NULL;

        CREATE TABLE IF NOT EXISTS realtime_events (
            sequence_no bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            event_id uuid NOT NULL UNIQUE,
            event_type varchar(120) NOT NULL,
            scope varchar(64) NOT NULL,
            audience_type varchar(20) NOT NULL,
            audience_key varchar(200) NULL,
            payload jsonb NOT NULL,
            occurred_at timestamptz NOT NULL,
            expires_at timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_realtime_events_replay
            ON realtime_events(sequence_no, audience_type, audience_key);
        CREATE INDEX IF NOT EXISTS ix_realtime_events_expiry ON realtime_events(expires_at);

        CREATE TABLE IF NOT EXISTS api_idempotency (
            username varchar(128) NOT NULL,
            command_type varchar(160) NOT NULL,
            idempotency_key varchar(200) NOT NULL,
            request_hash varchar(64) NOT NULL,
            status varchar(20) NOT NULL,
            response_status integer NULL,
            response_body jsonb NULL,
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            expires_at timestamptz NOT NULL,
            PRIMARY KEY (username, command_type, idempotency_key)
        );
        CREATE INDEX IF NOT EXISTS ix_api_idempotency_expiry ON api_idempotency(expires_at);

        CREATE TABLE IF NOT EXISTS messaging_dead_letters (
            id bigserial PRIMARY KEY,
            message_id uuid NOT NULL,
            source_queue varchar(120) NOT NULL,
            routing_key varchar(200) NOT NULL,
            attempts integer NOT NULL,
            last_error text NOT NULL,
            correlation_id varchar(160) NULL,
            envelope jsonb NOT NULL,
            failed_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            replayed_at timestamptz NULL,
            replayed_by varchar(128) NULL,
            version bigint NOT NULL DEFAULT 1
        );
        ALTER TABLE messaging_dead_letters ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 1;
        CREATE INDEX IF NOT EXISTS ix_messaging_dead_letters_open
            ON messaging_dead_letters(failed_at DESC) WHERE replayed_at IS NULL;

        INSERT INTO schema_migrations(version, description)
        VALUES ('002_pubsub_sse', 'Transactional integration outbox, inbox, idempotency and SSE event store')
        ON CONFLICT (version) DO NOTHING;
        """;
}
