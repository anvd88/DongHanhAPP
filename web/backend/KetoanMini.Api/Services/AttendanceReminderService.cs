using System.Data;
using KetoanMini.Api.Data;
using Npgsql;

namespace KetoanMini.Api.Services;

public sealed record AttendanceReminderRunResult(
    int Candidates, int Enqueued, int SuppressedByRequest, int Resolved, DateTime CompletedAtUtc);

/// <summary>
/// Reconcile bền cho nhắc thiếu giờ Ra. Ledger là nguồn sự thật/idempotency; push luôn được ghi vào
/// outbox trong cùng transaction với trạng thái đã enqueue nên restart/race không làm mất hoặc nhân đôi.
/// </summary>
public sealed class AttendanceReminderService(
    Database db,
    PushService push,
    IConfiguration config,
    ILogger<AttendanceReminderService> log)
{
    private sealed record Candidate(
        Guid EmployeeId, string Username, DateOnly WorkDate, Guid? LatestRequestId, string LatestRequestStatus);

    private sealed record LedgerRow(
        Guid EmployeeId, DateOnly WorkDate, string Status, Guid? RequestId, string NotificationId,
        DateTime? NotificationEnqueuedAt);

    public int LookbackDays => Math.Clamp(
        config.GetValue<int?>("AttendanceReminder:LookbackDays") ?? 31, 1, 366);

    public async Task<AttendanceReminderRunResult> ReconcileAsync(
        DateTime? nowUtcOverride = null, CancellationToken ct = default)
    {
        var nowUtc = (nowUtcOverride ?? DateTime.UtcNow).ToUniversalTime();
        var nowLocal = DateTime.SpecifyKind(nowUtc.AddHours(7), DateTimeKind.Unspecified);
        var today = DateOnly.FromDateTime(nowLocal);
        var from = today.AddDays(-LookbackDays);
        var to = today.AddDays(-1);
        if (to < from)
            return new AttendanceReminderRunResult(0, 0, 0, 0, nowUtc);

        await using var conn = await db.OpenAsync(ct);
        await using var tx = (NpgsqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var candidates = new List<Candidate>();
        await using (var reader = await conn.Cmd("""
            WITH activity AS (
                SELECT e.id AS employee_id,e.username,l.logical_work_date AS work_date,
                       BOOL_OR(l.loai='Vào') AS has_in,
                       BOOL_OR(l.loai='Ra') AS has_out
                FROM hr_employees e
                JOIN hr_effective_attendance_log l ON lower(l.username)=lower(e.username)
                WHERE e.status='Active' AND e.username<>''
                  AND l.logical_work_date BETWEEN @from AND @to
                GROUP BY e.id,e.username,l.logical_work_date
            )
            SELECT a.employee_id,a.username,a.work_date,
                   latest.id AS latest_request_id,COALESCE(latest.status,'') AS latest_request_status
            FROM activity a
            LEFT JOIN hr_shift_assignments sa
              ON sa.employee_id=a.employee_id AND sa.work_date=a.work_date
            LEFT JOIN hr_shifts s ON s.id=sa.shift_id
            LEFT JOIN LATERAL (
                SELECT r.id,r.status
                FROM hr_requests r
                WHERE r.employee_id=a.employee_id AND r.req_type='forgot_checkin'
                  AND r.payload->>'direction'='out'
                  AND r.payload->>'date'=to_char(a.work_date,'YYYY-MM-DD')
                ORDER BY r.created_at DESC,r.id DESC LIMIT 1
            ) latest ON TRUE
            WHERE a.has_in AND NOT a.has_out
              AND ((s.id IS NULL AND (a.work_date + 1) + TIME '06:00' <= @nowLocal)
                   OR (s.id IS NOT NULL AND
                       (a.work_date + CASE WHEN s.is_overnight THEN 1 ELSE 0 END)
                         + s.end_time + make_interval(mins => s.checkout_grace_minutes) <= @nowLocal))
              AND NOT EXISTS (
                  SELECT 1 FROM hr_payslips p
                  WHERE p.employee_id=a.employee_id AND p.published=TRUE
                    AND p.period=to_char(a.work_date,'YYYY-MM')
              )
            ORDER BY a.work_date,a.employee_id
            """, tx).With("@from", from).With("@to", to).With("@nowLocal", nowLocal)
            .ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                candidates.Add(new Candidate(
                    reader.Guid("employee_id"), reader.Str("username"), reader.DateOnly("work_date"),
                    reader.IsDBNull(reader.GetOrdinal("latest_request_id"))
                        ? null : reader.Guid("latest_request_id"),
                    reader.Str("latest_request_status")));
        }

        var existing = new Dictionary<(Guid EmployeeId, DateOnly WorkDate), LedgerRow>();
        await using (var reader = await conn.Cmd("""
            SELECT employee_id,work_date,status,request_id,notification_id,notification_enqueued_at
            FROM hr_attendance_reminders
            WHERE direction='out' AND work_date BETWEEN @from AND @to
            FOR UPDATE
            """, tx).With("@from", from).With("@to", to).ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var row = new LedgerRow(
                    reader.Guid("employee_id"), reader.DateOnly("work_date"), reader.Str("status"),
                    reader.IsDBNull(reader.GetOrdinal("request_id")) ? null : reader.Guid("request_id"),
                    reader.Str("notification_id"), reader.DtNull("notification_enqueued_at"));
                existing[(row.EmployeeId, row.WorkDate)] = row;
            }
        }

        var detected = candidates.Select(x => (x.EmployeeId, x.WorkDate)).ToHashSet();
        var resolved = 0;
        foreach (var row in existing.Values.Where(x => x.Status != "Resolved"
                                                        && !detected.Contains((x.EmployeeId, x.WorkDate))))
        {
            resolved += await conn.Cmd("""
                UPDATE hr_attendance_reminders
                   SET status='Resolved',last_checked_at=@checked,resolved_at=@checked,
                       resolution_source='reconciled'
                 WHERE employee_id=@employee AND work_date=@date AND direction='out'
                   AND status<>'Resolved'
                """, tx).With("@checked", nowUtc).With("@employee", row.EmployeeId)
                .With("@date", row.WorkDate).ExecuteNonQueryAsync(ct);
        }

        var enqueued = 0;
        var suppressed = 0;
        foreach (var candidate in candidates)
        {
            var suppress = candidate.LatestRequestStatus is "Pending" or "Approved" or "Resolved" or "Completed";
            var notificationId = candidate.LatestRequestId is { } requestId
                                 && candidate.LatestRequestStatus is "Rejected" or "Cancelled"
                ? $"attendance:missing-checkout:{candidate.WorkDate:yyyy-MM-dd}:retry:{requestId}"
                : $"attendance:missing-checkout:{candidate.WorkDate:yyyy-MM-dd}";
            var status = suppress ? "RequestCreated" : "Pending";

            await conn.Cmd("""
                INSERT INTO hr_attendance_reminders
                    (id,employee_id,username,work_date,direction,status,request_id,notification_id,
                     detected_at,last_checked_at,resolution_source)
                VALUES
                    (@id,@employee,@username,@date,'out',@status,@request,@notification,@checked,@checked,@source)
                ON CONFLICT (employee_id,work_date,direction) DO UPDATE
                   SET username=EXCLUDED.username,status=EXCLUDED.status,request_id=EXCLUDED.request_id,
                       notification_enqueued_at=CASE
                           WHEN hr_attendance_reminders.notification_id<>EXCLUDED.notification_id THEN NULL
                           ELSE hr_attendance_reminders.notification_enqueued_at END,
                       notification_id=EXCLUDED.notification_id,last_checked_at=EXCLUDED.last_checked_at,
                       resolved_at=NULL,resolution_source=EXCLUDED.resolution_source
                """, tx).With("@id", Guid.NewGuid()).With("@employee", candidate.EmployeeId)
                .With("@username", candidate.Username).With("@date", candidate.WorkDate)
                .With("@status", status).With("@request", (object?)candidate.LatestRequestId ?? DBNull.Value)
                .With("@notification", notificationId).With("@checked", nowUtc)
                .With("@source", suppress ? "request" : "missing_checkout").ExecuteNonQueryAsync(ct);

            if (suppress)
            {
                suppressed++;
                continue;
            }

            var shouldEnqueue = await conn.Cmd("""
                SELECT notification_enqueued_at IS NULL
                FROM hr_attendance_reminders
                WHERE employee_id=@employee AND work_date=@date AND direction='out' AND status='Pending'
                FOR UPDATE
                """, tx).With("@employee", candidate.EmployeeId).With("@date", candidate.WorkDate)
                .ExecuteScalarAsync(ct);
            if (shouldEnqueue is not true) continue;

            await push.EnqueueToUserAsync(conn, tx, candidate.Username, "Bạn chưa chấm giờ ra",
                $"Ngày {candidate.WorkDate:dd/MM/yyyy} đang thiếu giờ ra. Chạm để tạo đơn báo quên chấm công.",
                notificationId, "Requests", ct);
            await conn.Cmd("""
                UPDATE hr_attendance_reminders
                   SET notification_enqueued_at=@checked,last_checked_at=@checked
                 WHERE employee_id=@employee AND work_date=@date AND direction='out'
                   AND status='Pending' AND notification_id=@notification
                """, tx).With("@checked", nowUtc).With("@employee", candidate.EmployeeId)
                .With("@date", candidate.WorkDate).With("@notification", notificationId)
                .ExecuteNonQueryAsync(ct);
            enqueued++;
        }

        await tx.CommitAsync(ct);
        var result = new AttendanceReminderRunResult(candidates.Count, enqueued, suppressed, resolved, nowUtc);
        log.LogInformation(
            "Attendance reminder reconcile: candidates={Candidates}, enqueued={Enqueued}, suppressed={Suppressed}, resolved={Resolved}, from={From}, to={To}",
            result.Candidates, result.Enqueued, result.SuppressedByRequest, result.Resolved, from, to);
        return result;
    }
}

public sealed class AttendanceReminderWorker(
    AttendanceReminderService service,
    IConfiguration config,
    ILogger<AttendanceReminderWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!(config.GetValue<bool?>("AttendanceReminder:Enabled") ?? true))
        {
            log.LogInformation("Attendance reminder worker is disabled by configuration.");
            return;
        }

        var intervalMinutes = Math.Clamp(
            config.GetValue<int?>("AttendanceReminder:IntervalMinutes") ?? 15, 1, 1440);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        do
        {
            try
            {
                await service.ReconcileAsync(ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Attendance reminder reconcile failed; it will retry on the next interval.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
