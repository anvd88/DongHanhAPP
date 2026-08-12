using KetoanMini.Api.Data;

namespace KetoanMini.Api.Services;

/// <summary>
/// Enforces the retention limit for biometric templates waiting for HR verification. The first sweep
/// runs as soon as the host starts and subsequent sweeps run hourly, so cleanup does not depend on a
/// user opening an enrollment endpoint.
/// </summary>
public sealed class FaceEnrollmentCleanupService(
    Database db,
    ILogger<FaceEnrollmentCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transient database outage must not kill the worker permanently. The next hourly
                // sweep retries every still-expired request and therefore catches up automatically.
                logger.LogWarning(ex, "Could not remove expired face-enrollment templates; will retry.");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Marks due requests expired and deletes every staged embedding belonging to an expired request
    /// in one transaction. Public for deterministic operational/integration verification without
    /// waiting for the hourly timer.
    /// </summary>
    public async Task<(int ExpiredRequests, int DeletedSamples)> SweepAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var expired = await conn.Cmd(
            """
            UPDATE cham_cong_face_enrollments
               SET status='expired',
                   reviewed_at=CURRENT_TIMESTAMP,
                   review_note='Tự động hết hạn sau 14 ngày.'
             WHERE status='pending' AND expires_at <= CURRENT_TIMESTAMP
            """, tx).ExecuteNonQueryAsync(ct);

        var deleted = await conn.Cmd(
            """
            DELETE FROM cham_cong_face_enrollment_samples s
             USING cham_cong_face_enrollments r
             WHERE s.request_id=r.id AND r.status='expired'
            """, tx).ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);

        if (expired > 0 || deleted > 0)
            logger.LogInformation(
                "Expired {RequestCount} face-enrollment requests and deleted {SampleCount} staged biometric templates.",
                expired, deleted);

        return (expired, deleted);
    }
}
