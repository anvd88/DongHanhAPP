using KetoanMini.Api.Data;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Installs lightweight PostgreSQL statement triggers which publish the affected UI scope.
/// PostgreSQL keeps notifications until the surrounding transaction commits, so clients never
/// observe an event for a rolled-back write.
/// </summary>
public static class DatabaseChangePublisher
{
    public const string ChannelName = "ketoanmini_changes";

    // Keep this list aligned with the scopes consumed by realtime.ts. A statement-level trigger
    // emits at most one notification per SQL statement, even for bulk imports/updates.
    internal const string TriggerSql = """
        CREATE OR REPLACE FUNCTION public.ketoanmini_publish_change()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            scope_name text;
        BEGIN
            FOREACH scope_name IN ARRAY TG_ARGV
            LOOP
                PERFORM pg_notify('ketoanmini_changes', scope_name);
            END LOOP;
            RETURN NULL;
        END;
        $function$;

        DO $block$
        DECLARE
            target record;
            trigger_args text;
        BEGIN
            FOR target IN
                SELECT *
                FROM (VALUES
                    ('documents',            ARRAY['data']::text[]),
                    ('document_lines',        ARRAY['data']::text[]),
                    ('payments',              ARRAY['data']::text[]),
                    ('customers',             ARRAY['data']::text[]),
                    ('customer_aliases',       ARRAY['data']::text[]),
                    ('gia_cong_phieu',        ARRAY['data']::text[]),
                    ('gia_cong_hang_hoa',     ARRAY['data']::text[]),
                    ('cham_cong_face',        ARRAY['data', 'hr']::text[]),
                    ('cham_cong_log',         ARRAY['data', 'hr']::text[]),

                    ('app_users',             ARRAY['presence']::text[]),
                    ('user_sessions',         ARRAY['presence']::text[]),
                    ('user_roles',            ARRAY['presence']::text[]),

                    ('hr_departments',        ARRAY['hr']::text[]),
                    ('hr_employees',          ARRAY['hr']::text[]),
                    ('hr_contracts',          ARRAY['hr']::text[]),
                    ('hr_payslips',           ARRAY['hr']::text[]),
                    ('hr_leave_balances',     ARRAY['hr']::text[]),
                    ('hr_documents',          ARRAY['hr']::text[]),
                    ('hr_shifts',             ARRAY['hr']::text[]),
                    ('hr_shift_assignments',  ARRAY['hr']::text[]),
                    ('hr_requests',           ARRAY['hr']::text[]),
                    ('hr_request_approvals',  ARRAY['hr']::text[]),
                    ('hr_request_attachments', ARRAY['hr']::text[]),
                    ('hr_approval_delegations', ARRAY['hr']::text[]),
                    ('hr_locations',          ARRAY['hr']::text[]),
                    ('hr_holidays',           ARRAY['hr']::text[]),
                    ('cham_cong_offline',     ARRAY['hr']::text[]),
                    ('cham_cong_qr_sites',    ARRAY['hr']::text[]),
                    ('web_system_settings',   ARRAY['hr']::text[]),

                    ('work_tasks',            ARRAY['tasks']::text[]),
                    ('work_task_events',      ARRAY['tasks']::text[]),

                    ('app_portal_posts',      ARRAY['portal']::text[]),
                    ('app_portal_about',      ARRAY['portal']::text[]),

                    ('app_config',            ARRAY['config']::text[]),
                    ('audit_logs',            ARRAY['audit']::text[]),

                    ('hr_onboarding_tasks',   ARRAY['talent']::text[]),
                    ('hr_performance_goals',  ARRAY['talent']::text[]),
                    ('hr_performance_reviews', ARRAY['talent']::text[]),
                    ('hr_training_courses',   ARRAY['talent']::text[]),
                    ('hr_training_enrollments', ARRAY['talent']::text[]),
                    ('hr_employee_benefits',  ARRAY['talent']::text[]),
                    ('hr_employee_rewards',   ARRAY['talent']::text[])
                ) AS watched(table_name, scopes)
            LOOP
                -- Some optional modules create their tables after the baseline migration. Missing
                -- tables are skipped so one disabled module cannot disable realtime for all others.
                IF to_regclass(format('public.%I', target.table_name)) IS NULL THEN
                    CONTINUE;
                END IF;

                SELECT string_agg(quote_literal(value), ', ')
                INTO trigger_args
                FROM unnest(target.scopes) AS value;

                EXECUTE format(
                    'DROP TRIGGER IF EXISTS ketoanmini_publish_change ON public.%I',
                    target.table_name);
                EXECUTE format(
                    'CREATE TRIGGER ketoanmini_publish_change ' ||
                    'AFTER INSERT OR UPDATE OR DELETE ON public.%I ' ||
                    'FOR EACH STATEMENT EXECUTE FUNCTION public.ketoanmini_publish_change(%s)',
                    target.table_name,
                    trigger_args);
            END LOOP;
        END;
        $block$;
        """;

    public static async Task EnsureAsync(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd(TriggerSql).ExecuteNonQueryAsync(ct);
    }
}
