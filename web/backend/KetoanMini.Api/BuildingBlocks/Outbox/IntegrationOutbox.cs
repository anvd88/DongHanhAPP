using System.Text.Json;
using KetoanMini.Api.BuildingBlocks.Messaging;
using KetoanMini.Api.Data;
using Npgsql;

namespace KetoanMini.Api.BuildingBlocks.Outbox;

public sealed record PendingIntegrationMessage(
    Guid Id, string EventType, string RoutingKey, string Payload, string Headers, int Attempts);
public sealed record OutboxMetrics(long Pending, double OldestAgeSeconds, int MaxAttempts, long DeadLetters);

public sealed class IntegrationOutbox(Database db)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<Guid> EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string routingKey,
        IntegrationEventEnvelope envelope,
        CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(envelope, IntegrationEventJson.Options);
        await new NpgsqlCommand("""
            INSERT INTO integration_outbox
                (id,event_type,routing_key,aggregate_type,aggregate_id,aggregate_version,payload,headers,occurred_at)
            VALUES
                (@id,@type,@route,@aggregateType,@aggregateId,@version,@payload::jsonb,'{}'::jsonb,@occurred)
            ON CONFLICT (id) DO NOTHING
            """, connection, transaction)
        {
            Parameters =
            {
                new("@id", envelope.EventId), new("@type", envelope.EventType),
                new("@route", routingKey), new("@aggregateType", (object?)null ?? DBNull.Value),
                new("@aggregateId", (object?)envelope.AggregateId ?? DBNull.Value),
                new("@version", (object?)envelope.AggregateVersion ?? DBNull.Value),
                // Cùng lý do như RealtimeEventStore.AppendAsync: timestamptz của Npgsql chỉ nhận
                // DateTimeOffset lệch 0, mà phong bì phát lại từ DLQ có thể mang mốc lệch +07:00.
                new("@payload", json), new("@occurred", envelope.OccurredAt.ToUniversalTime()),
            }
        }.ExecuteNonQueryAsync(ct);
        return envelope.EventId;
    }

    public async Task<IReadOnlyList<PendingIntegrationMessage>> ClaimAsync(int max, CancellationToken ct)
    {
        var result = new List<PendingIntegrationMessage>();
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var reader = await new NpgsqlCommand("""
            WITH claimed AS (
                SELECT id FROM integration_outbox
                WHERE published_at IS NULL
                  AND available_at <= CURRENT_TIMESTAMP
                  AND (locked_until IS NULL OR locked_until < CURRENT_TIMESTAMP)
                ORDER BY occurred_at,id
                FOR UPDATE SKIP LOCKED
                LIMIT @max
            )
            UPDATE integration_outbox o
               SET attempts=o.attempts+1, locked_until=CURRENT_TIMESTAMP+@lease
              FROM claimed c WHERE o.id=c.id
            RETURNING o.id,o.event_type,o.routing_key,o.payload::text,o.headers::text,o.attempts
            """, conn, tx)
        {
            Parameters = { new("@max", max), new("@lease", Lease) }
        }.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetInt32(5)));
        await reader.DisposeAsync();
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task MarkPublishedAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            UPDATE integration_outbox SET published_at=CURRENT_TIMESTAMP,locked_until=NULL,last_error=''
            WHERE id=@id
            """).With("@id", id).ExecuteNonQueryAsync(ct);
    }

    public async Task RetryAsync(Guid id, int attempts, string error, CancellationToken ct)
    {
        var seconds = Math.Min(300, Math.Pow(2, Math.Min(attempts, 8))) + Random.Shared.NextDouble();
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            UPDATE integration_outbox SET locked_until=NULL,available_at=CURRENT_TIMESTAMP+@delay,last_error=@error
            WHERE id=@id AND published_at IS NULL
            """).With("@id", id).With("@delay", TimeSpan.FromSeconds(seconds))
            .With("@error", error.Length > 1000 ? error[..1000] : error).ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Dọn phần ĐÃ XONG của outbox/inbox. Hai bảng này trước đây không có ai dọn: mỗi lệnh ghi
    /// nghiệp vụ để lại một dòng vĩnh viễn, nên chúng chỉ có thể phình mãi (DB kiểm thử đã tới
    /// 805.000 dòng / 2,1 GB) và mọi INSERT sau đó phải trả tiền cho chỉ mục ngày càng nặng.
    ///
    /// Chỉ xoá dòng ĐÃ published/completed: việc chưa gửi được là dữ liệu sống, không bao giờ đụng
    /// tới. messaging_dead_letters cũng không đụng — đó là hồ sơ điều tra.
    /// Xoá theo lô để không khoá bảng bằng một giao dịch khổng lồ; SKIP LOCKED để nhiều tiến trình
    /// (hoặc nhiều host kiểm thử chạy song song) dọn cùng lúc mà không xếp hàng chờ nhau.
    /// </summary>
    public async Task<int> PurgeCompletedAsync(TimeSpan keep, CancellationToken ct)
    {
        const int batch = 10_000;
        const int maxBatches = 100;
        var total = 0;
        await using var conn = await db.OpenAsync(ct);
        foreach (var sql in new[]
        {
            """
            DELETE FROM integration_outbox WHERE id IN (
                SELECT id FROM integration_outbox
                WHERE published_at IS NOT NULL AND published_at < CURRENT_TIMESTAMP - @keep
                LIMIT @batch FOR UPDATE SKIP LOCKED)
            """,
            """
            DELETE FROM inbox_messages WHERE ctid IN (
                SELECT ctid FROM inbox_messages
                WHERE completed_at IS NOT NULL AND completed_at < CURRENT_TIMESTAMP - @keep
                LIMIT @batch FOR UPDATE SKIP LOCKED)
            """,
        })
            for (var i = 0; i < maxBatches; i++)
            {
                var removed = await conn.Cmd(sql).With("@keep", keep).With("@batch", batch)
                    .ExecuteNonQueryAsync(ct);
                total += removed;
                if (removed < batch) break;
            }
        return total;
    }

    public async Task<OutboxMetrics> MetricsAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var reader = await conn.Cmd("""
            SELECT COUNT(*) FILTER (WHERE published_at IS NULL),
                   COALESCE(EXTRACT(EPOCH FROM (CURRENT_TIMESTAMP-MIN(occurred_at) FILTER (WHERE published_at IS NULL))),0),
                   COALESCE(MAX(attempts) FILTER (WHERE published_at IS NULL),0),
                   (SELECT COUNT(*) FROM messaging_dead_letters WHERE replayed_at IS NULL)
            FROM integration_outbox
            """).ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new(reader.GetInt64(0), reader.GetDouble(1), reader.GetInt32(2), reader.GetInt64(3));
    }
}
