BEGIN;

CREATE TABLE IF NOT EXISTS integration_outbox (
    id uuid PRIMARY KEY, event_type varchar(160) NOT NULL, routing_key varchar(200) NOT NULL,
    aggregate_type varchar(100), aggregate_id varchar(160), aggregate_version bigint,
    payload jsonb NOT NULL, headers jsonb NOT NULL DEFAULT '{}'::jsonb,
    occurred_at timestamptz NOT NULL, available_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    attempts integer NOT NULL DEFAULT 0, locked_until timestamptz, published_at timestamptz,
    last_error text NOT NULL DEFAULT '', bridge_key varchar(200)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_integration_outbox_bridge ON integration_outbox(bridge_key) WHERE bridge_key IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_integration_outbox_ready ON integration_outbox(available_at,occurred_at) WHERE published_at IS NULL;
-- Vong don dinh ky: giu cot published_at co chi muc rieng, neu khong moi luot don la mot lan quet ca bang.
CREATE INDEX IF NOT EXISTS ix_integration_outbox_purge ON integration_outbox(published_at) WHERE published_at IS NOT NULL;

CREATE TABLE IF NOT EXISTS inbox_messages (
    consumer_name varchar(120) NOT NULL, message_id uuid NOT NULL,
    received_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP, completed_at timestamptz,
    correlation_id varchar(160), PRIMARY KEY(consumer_name,message_id)
);
CREATE INDEX IF NOT EXISTS ix_inbox_messages_purge ON inbox_messages(completed_at) WHERE completed_at IS NOT NULL;

CREATE TABLE IF NOT EXISTS realtime_events (
    sequence_no bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, event_id uuid NOT NULL UNIQUE,
    event_type varchar(120) NOT NULL, scope varchar(64) NOT NULL, audience_type varchar(20) NOT NULL,
    audience_key varchar(200), payload jsonb NOT NULL, occurred_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_realtime_events_replay ON realtime_events(sequence_no,audience_type,audience_key);
CREATE INDEX IF NOT EXISTS ix_realtime_events_expiry ON realtime_events(expires_at);

CREATE TABLE IF NOT EXISTS api_idempotency (
    username varchar(128) NOT NULL, command_type varchar(160) NOT NULL,
    idempotency_key varchar(200) NOT NULL, request_hash varchar(64) NOT NULL,
    status varchar(20) NOT NULL, response_status integer, response_body jsonb,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP, expires_at timestamptz NOT NULL,
    PRIMARY KEY(username,command_type,idempotency_key)
);
CREATE INDEX IF NOT EXISTS ix_api_idempotency_expiry ON api_idempotency(expires_at);

CREATE TABLE IF NOT EXISTS messaging_dead_letters (
    id bigserial PRIMARY KEY, message_id uuid NOT NULL, source_queue varchar(120) NOT NULL,
    routing_key varchar(200) NOT NULL, attempts integer NOT NULL, last_error text NOT NULL,
    correlation_id varchar(160), envelope jsonb NOT NULL,
    failed_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    replayed_at timestamptz, replayed_by varchar(128), version bigint NOT NULL DEFAULT 1
);
ALTER TABLE messaging_dead_letters ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 1;
CREATE INDEX IF NOT EXISTS ix_messaging_dead_letters_open ON messaging_dead_letters(failed_at DESC) WHERE replayed_at IS NULL;

-- First aggregate migrated incrementally to explicit optimistic concurrency. The table may be
-- created later by its module on a fresh install, hence the guarded ALTER.
ALTER TABLE IF EXISTS cash_fund_manual_entries ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 1;

INSERT INTO schema_migrations(version,description)
VALUES('002_pubsub_sse','Transactional integration outbox, inbox, idempotency and SSE event store')
ON CONFLICT(version) DO NOTHING;

COMMIT;
