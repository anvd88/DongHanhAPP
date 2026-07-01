using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Data;

public static class PostgresSchema
{
    public static async Task EnsureAsync(Database db, IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        await db.EnsureDatabaseExistsAsync(ct);
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd(SchemaSql).ExecuteNonQueryAsync(ct);
        await SeedAdminAsync(conn, config, logger, ct);
    }

    private static async Task SeedAdminAsync(NpgsqlConnection conn, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var hasAdmin = await conn.Cmd(
            "SELECT 1 FROM app_users WHERE role = 'Admin' AND is_deleted = FALSE LIMIT 1")
            .ExecuteScalarAsync(ct);
        if (hasAdmin is not null and not DBNull)
            return;

        var username = (config["Bootstrap:AdminUsername"] ?? "admin").Trim();
        var password = config["Bootstrap:AdminPassword"] ?? "admin123";
        var fullName = (config["Bootstrap:AdminFullName"] ?? "Administrator").Trim();
        var email = (config["Bootstrap:AdminEmail"] ?? "").Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Khong seed admin PostgreSQL vi Bootstrap:AdminUsername/AdminPassword dang rong.");
            return;
        }

        await conn.Cmd("""
            INSERT INTO app_users
                (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES
                (@id, @username, @fullName, @email, 'Admin', @passwordHash, TRUE, 'Approved', CURRENT_TIMESTAMP, 'system', CURRENT_TIMESTAMP, FALSE)
            ON CONFLICT (username) DO UPDATE SET
                role = 'Admin',
                is_active = TRUE,
                is_deleted = FALSE,
                approval_status = 'Approved',
                approved_at = COALESCE(app_users.approved_at, CURRENT_TIMESTAMP);
            """)
            .With("@id", Guid.NewGuid())
            .With("@username", username)
            .With("@fullName", string.IsNullOrWhiteSpace(fullName) ? username : fullName)
            .With("@email", email)
            .With("@passwordHash", PasswordHasher.Hash(password))
            .ExecuteNonQueryAsync(ct);

        logger.LogInformation("Da seed tai khoan admin mac dinh cho PostgreSQL: {Username}", username);
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS app_users (
            id uuid NOT NULL PRIMARY KEY,
            username varchar(128) NOT NULL UNIQUE,
            full_name varchar(256) NOT NULL DEFAULT '',
            email varchar(256) NOT NULL DEFAULT '',
            role varchar(32) NOT NULL DEFAULT 'User',
            password_hash text NOT NULL,
            is_active boolean NOT NULL DEFAULT TRUE,
            approval_status varchar(32) NOT NULL DEFAULT 'Approved',
            approved_at timestamptz NULL,
            approved_by varchar(128) NOT NULL DEFAULT '',
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            is_deleted boolean NOT NULL DEFAULT FALSE
        );

        ALTER TABLE app_users ADD COLUMN IF NOT EXISTS email varchar(256) NOT NULL DEFAULT '';
        ALTER TABLE app_users ADD COLUMN IF NOT EXISTS is_deleted boolean NOT NULL DEFAULT FALSE;

        CREATE TABLE IF NOT EXISTS customers (
            id uuid NOT NULL PRIMARY KEY,
            name varchar(256) NOT NULL,
            tax_code varchar(64) NOT NULL DEFAULT '',
            phone varchar(64) NOT NULL DEFAULT '',
            address text NOT NULL DEFAULT '',
            is_active boolean NOT NULL DEFAULT TRUE,
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS documents (
            id uuid NOT NULL PRIMARY KEY,
            voucher_no varchar(64) NOT NULL,
            doc_date date NOT NULL,
            customer_id uuid NULL REFERENCES customers(id) ON DELETE SET NULL,
            customer_name varchar(256) NOT NULL DEFAULT '',
            customer_input_name varchar(256) NOT NULL DEFAULT '',
            content text NOT NULL DEFAULT '',
            note text NOT NULL DEFAULT '',
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS document_lines (
            id bigserial NOT NULL PRIMARY KEY,
            document_id uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            line_no integer NOT NULL DEFAULT 0,
            line_content text NOT NULL DEFAULT '',
            category varchar(128) NOT NULL DEFAULT '',
            spec text NOT NULL DEFAULT '',
            quantity numeric(18,2) NOT NULL DEFAULT 0,
            unit_price numeric(18,2) NOT NULL DEFAULT 0,
            note text NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS payments (
            id uuid NOT NULL PRIMARY KEY,
            customer_id uuid NULL REFERENCES customers(id) ON DELETE SET NULL,
            customer_name varchar(256) NOT NULL DEFAULT '',
            customer_input_name varchar(256) NOT NULL DEFAULT '',
            amount numeric(18,2) NOT NULL DEFAULT 0,
            pay_date date NOT NULL DEFAULT CURRENT_DATE,
            note text NOT NULL DEFAULT '',
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS customer_aliases (
            id bigserial NOT NULL PRIMARY KEY,
            customer_id uuid NULL REFERENCES customers(id) ON DELETE CASCADE,
            customer_name varchar(256) NOT NULL DEFAULT '',
            alias varchar(256) NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS audit_logs (
            id bigserial NOT NULL PRIMARY KEY,
            user_id uuid NULL,
            occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            username varchar(128) NOT NULL DEFAULT '',
            action varchar(256) NOT NULL DEFAULT '',
            entity varchar(128) NOT NULL DEFAULT '',
            entity_name varchar(256) NOT NULL DEFAULT '',
            details text NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS user_sessions (
            session_token varchar(128) NOT NULL PRIMARY KEY,
            username varchar(128) NOT NULL DEFAULT '',
            machine_name varchar(128) NOT NULL DEFAULT '',
            started_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            last_seen timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            ended_at timestamptz NULL,
            is_active boolean NOT NULL DEFAULT TRUE,
            end_reason text NOT NULL DEFAULT '',
            client_kind varchar(20) NOT NULL DEFAULT 'Desktop'
        );

        ALTER TABLE user_sessions ADD COLUMN IF NOT EXISTS client_kind varchar(20) NOT NULL DEFAULT 'Desktop';

        CREATE TABLE IF NOT EXISTS app_releases (
            id bigserial NOT NULL PRIMARY KEY,
            version varchar(64) NOT NULL DEFAULT '',
            release_notes text NOT NULL DEFAULT '',
            is_mandatory boolean NOT NULL DEFAULT FALSE,
            is_published boolean NOT NULL DEFAULT FALSE,
            published_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            published_by varchar(128) NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS work_access_requests (
            id bigserial NOT NULL PRIMARY KEY,
            username varchar(128) NOT NULL DEFAULT '',
            approved_by varchar(128) NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS password_reset_requests (
            id bigserial NOT NULL PRIMARY KEY,
            username varchar(128) NOT NULL DEFAULT '',
            resolved_by varchar(128) NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS registration_codes (
            id bigserial NOT NULL PRIMARY KEY,
            created_by varchar(128) NOT NULL DEFAULT '',
            used_by varchar(128) NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS app_settings (
            setting_key varchar(128) NOT NULL PRIMARY KEY,
            setting_value text NOT NULL DEFAULT '',
            updated_by varchar(128) NOT NULL DEFAULT '',
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS web_verified_users (
            username varchar(128) NOT NULL PRIMARY KEY,
            granted_by varchar(128) NOT NULL DEFAULT '',
            granted_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS web_diamond_members (
            username varchar(128) NOT NULL PRIMARY KEY,
            granted_by varchar(128) NOT NULL DEFAULT '',
            granted_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE INDEX IF NOT EXISTS ix_app_users_username_live ON app_users (username) WHERE is_deleted = FALSE;
        CREATE INDEX IF NOT EXISTS ix_customers_name_active ON customers (name) WHERE is_active = TRUE;
        CREATE INDEX IF NOT EXISTS ix_documents_customer ON documents (customer_id, doc_date DESC);
        CREATE INDEX IF NOT EXISTS ix_documents_date ON documents (doc_date DESC, voucher_no DESC);
        CREATE INDEX IF NOT EXISTS ix_document_lines_document ON document_lines (document_id, line_no);
        CREATE INDEX IF NOT EXISTS ix_payments_customer ON payments (customer_id, pay_date DESC);
        CREATE INDEX IF NOT EXISTS ix_audit_logs_occurred ON audit_logs (occurred_at DESC);
        CREATE INDEX IF NOT EXISTS ix_user_sessions_presence ON user_sessions (username, is_active, last_seen DESC);
        """;
}
