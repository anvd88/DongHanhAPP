using System.Security.Cryptography;
using System.Text;
using KetoanMini.Api.Data;
using Npgsql;

namespace KetoanMini.Api.BuildingBlocks.Idempotency;

public enum IdempotencyDecision { Execute, Replay, Conflict }
public sealed record IdempotencyLease(IdempotencyDecision Decision, string RequestHash,
    int? ResponseStatus = null, string? ResponseBody = null);

/// <summary>Transaction-scoped Idempotency-Key primitive for important commands.</summary>
public sealed class IdempotencyStore
{
    public static string Hash(string canonicalRequest) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))).ToLowerInvariant();

    public async Task<IdempotencyLease> BeginAsync(NpgsqlConnection conn, NpgsqlTransaction tx,
        string username, string commandType, string key, string canonicalRequest,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
            throw new ArgumentException("Idempotency-Key is required and must be at most 200 characters.", nameof(key));
        var hash = Hash(canonicalRequest);
        var inserted = await conn.Cmd("""
            INSERT INTO api_idempotency
                (username,command_type,idempotency_key,request_hash,status,expires_at)
            VALUES (@username,@command,@key,@hash,'processing',CURRENT_TIMESTAMP+INTERVAL '7 days')
            ON CONFLICT DO NOTHING
            """, tx).With("@username", username).With("@command", commandType)
            .With("@key", key).With("@hash", hash).ExecuteNonQueryAsync(ct);
        if (inserted > 0) return new(IdempotencyDecision.Execute, hash);

        await using var reader = await conn.Cmd("""
            SELECT request_hash,status,response_status,response_body::text
            FROM api_idempotency
            WHERE username=@username AND command_type=@command AND idempotency_key=@key
            FOR UPDATE
            """, tx).With("@username", username).With("@command", commandType).With("@key", key)
            .ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Idempotency row disappeared.");
        var existingHash = reader.GetString(0);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(existingHash), Encoding.ASCII.GetBytes(hash)))
            return new(IdempotencyDecision.Conflict, hash);
        if (reader.GetString(1) == "completed")
            return new(IdempotencyDecision.Replay, hash,
                reader.IsDBNull(2) ? 200 : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3));
        // The unique INSERT waits for an uncommitted owner. A committed row still marked processing
        // means the owning transaction intentionally did not complete and must be investigated.
        return new(IdempotencyDecision.Conflict, hash);
    }

    public Task CompleteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string username,
        string commandType, string key, int responseStatus, string responseBody, CancellationToken ct = default)
        => conn.Cmd("""
            UPDATE api_idempotency SET status='completed',response_status=@status,response_body=@body::jsonb
            WHERE username=@username AND command_type=@command AND idempotency_key=@key
            """, tx).With("@status", responseStatus).With("@body", responseBody)
            .With("@username", username).With("@command", commandType).With("@key", key)
            .ExecuteNonQueryAsync(ct);
}

public sealed class IdempotencyRetentionWorker(Database db, ILogger<IdempotencyRetentionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = await db.OpenAsync(stoppingToken);
                var removed = await conn.Cmd("DELETE FROM api_idempotency WHERE expires_at<CURRENT_TIMESTAMP")
                    .ExecuteNonQueryAsync(stoppingToken);
                if (removed > 0) logger.LogInformation("Removed {Count} expired idempotency records.", removed);
            }
            catch (Exception ex) { logger.LogWarning("Idempotency cleanup failed: {Message}", ex.Message); }
            try { await Task.Delay(TimeSpan.FromHours(6), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
