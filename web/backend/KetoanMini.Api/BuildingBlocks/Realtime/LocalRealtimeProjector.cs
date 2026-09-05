using System.Text.Json;
using KetoanMini.Api.BuildingBlocks.Messaging;
using KetoanMini.Api.BuildingBlocks.Outbox;
using KetoanMini.Api.Data;
using Microsoft.Extensions.Options;

namespace KetoanMini.Api.BuildingBlocks.Realtime;

/// <summary>
/// Development/degraded transport used only when RabbitMQ is explicitly disabled. It preserves the
/// PostgreSQL outbox/inbox transaction boundary and lets SSE be exercised without pretending that
/// this is the production Pub/Sub topology.
/// </summary>
public sealed class LocalRealtimeProjector(
    Database db,
    IntegrationOutbox outbox,
    RealtimeEventStore events,
    RedisRealtimeCoordinator redis,
    OutboxSignal outboxSignal,
    IOptions<Messaging.RabbitMqOptions> rabbit,
    IOptions<RealtimeOptions> realtime,
    ILogger<LocalRealtimeProjector> logger) : BackgroundService
{
    /// <summary>Sau ngần này lần thất bại, một thông điệp bị coi là hỏng vĩnh viễn và chuyển sang DLQ.</summary>
    private const int MaxAttempts = 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (rabbit.Value.Enabled || !realtime.Value.SseEnabled) return;
        logger.LogWarning("RabbitMQ is disabled: using the local durable outbox projector for SSE development mode.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await outbox.ClaimAsync(50, stoppingToken);
                foreach (var message in batch)
                {
                    // MỖI thông điệp một lớp bảo vệ riêng. Trước đây một dòng payload hỏng ném lỗi ra
                    // tận vòng ngoài: cả lô bị bỏ dở, mà lô luôn lấy dòng CŨ NHẤT trước — nên đúng một
                    // dòng rác đủ để đóng băng toàn bộ realtime vĩnh viễn (đo thật trên DB kiểm thử:
                    // 8 dòng 'test.v1' chặn 392 sự kiện thật suốt gần ba ngày, chỉ có một dòng log
                    // cảnh báo). Giờ dòng hỏng tự lùi theo cấp số nhân rồi rơi vào DLQ, hàng đợi chảy tiếp.
                    try
                    {
                        await ProjectAsync(message, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        await FailAsync(message, ex, stoppingToken);
                    }
                }
                // Chuông từ pg_notify (trigger cầu nối + BusinessEventWriter) cắt nhịp chờ này ngay khi
                // giao dịch nghiệp vụ commit; một giây chỉ còn là lưới an toàn khi LISTEN đứt.
                if (batch.Count == 0) await outboxSignal.WaitAsync(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Local realtime projector failed; durable rows remain pending: {Message}", ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ProjectAsync(PendingIntegrationMessage message, CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(
            message.Payload, IntegrationEventJson.Options)
            ?? throw new JsonException("Invalid integration-event envelope.");
        if (envelope.EventId == Guid.Empty || string.IsNullOrWhiteSpace(envelope.EventType))
            throw new JsonException("Integration-event envelope is missing eventId/eventType.");

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var inserted = await conn.Cmd("""
            INSERT INTO inbox_messages(consumer_name,message_id,correlation_id)
            VALUES ('realtime-projection.local',@id,@correlation)
            ON CONFLICT DO NOTHING
            """, tx).With("@id", envelope.EventId)
            .With("@correlation", (object?)envelope.CorrelationId ?? DBNull.Value)
            .ExecuteNonQueryAsync(ct);
        long? cursor = null;
        if (inserted > 0)
        {
            cursor = await events.AppendAsync(conn, tx, envelope, ct);
            await conn.Cmd("""
                UPDATE inbox_messages SET completed_at=CURRENT_TIMESTAMP
                WHERE consumer_name='realtime-projection.local' AND message_id=@id
                """, tx).With("@id", envelope.EventId).ExecuteNonQueryAsync(ct);
            // Đánh thức luồng SSE của MỌI tiến trình đang chạy (kể cả bản chạy song song
            // không có Redis). Notification chỉ được gửi khi COMMIT thành công, nên không
            // ai bị đánh thức để đọc một dòng chưa tồn tại.
            if (cursor.HasValue)
                await conn.Cmd("SELECT pg_notify(@channel,@payload)", tx)
                    .With("@channel", PostgresWakeListener.RealtimeWakeChannel)
                    .With("@payload", cursor.Value.ToString())
                    .ExecuteNonQueryAsync(ct);
        }
        await conn.Cmd("""
            UPDATE integration_outbox SET published_at=CURRENT_TIMESTAMP,locked_until=NULL,last_error=''
            WHERE id=@id
            """, tx).With("@id", message.Id).ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        if (cursor.HasValue) await redis.PublishWakeAsync(cursor.Value);
    }

    /// <summary>
    /// Hết số lần thử thì chuyển sang messaging_dead_letters (có endpoint quản trị để xem và phát lại)
    /// và đánh dấu dòng outbox đã xử lý — bằng MỘT giao dịch, để không bao giờ vừa mất dấu vết vừa
    /// mất thông điệp. Chưa hết số lần thì chỉ lùi lịch thử lại.
    /// </summary>
    private async Task FailAsync(PendingIntegrationMessage message, Exception error, CancellationToken ct)
    {
        var reason = error.Message.Length > 2000 ? error.Message[..2000] : error.Message;
        if (message.Attempts < MaxAttempts)
        {
            logger.LogWarning("Realtime projection of {EventId} failed (attempt {Attempt}): {Message}",
                message.Id, message.Attempts, reason);
            await outbox.RetryAsync(message.Id, message.Attempts, reason, ct);
            return;
        }

        logger.LogError("Realtime projection of {EventId} failed {Attempt} times; moving it to the DLQ: {Message}",
            message.Id, message.Attempts, reason);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.Cmd("""
            INSERT INTO messaging_dead_letters
                (message_id,source_queue,routing_key,attempts,last_error,correlation_id,envelope)
            VALUES (@id,'realtime-projection.local',@route,@attempts,@error,NULL,@envelope::jsonb)
            """, tx).With("@id", message.Id).With("@route", message.RoutingKey)
            .With("@attempts", message.Attempts).With("@error", reason)
            .With("@envelope", message.Payload).ExecuteNonQueryAsync(ct);
        await conn.Cmd("""
            UPDATE integration_outbox SET published_at=CURRENT_TIMESTAMP,locked_until=NULL,last_error=@error
            WHERE id=@id
            """, tx).With("@id", message.Id).With("@error", reason).ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
}
