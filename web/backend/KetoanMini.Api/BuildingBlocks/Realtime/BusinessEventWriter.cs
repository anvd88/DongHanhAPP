using System.Text.Json;
using KetoanMini.Api.BuildingBlocks.Messaging;
using KetoanMini.Api.BuildingBlocks.Outbox;
using KetoanMini.Api.Data;
using KetoanMini.Api.Realtime;
using Npgsql;

namespace KetoanMini.Api.BuildingBlocks.Realtime;

/// <summary>
/// Transport-neutral business-event writer. New/refactored commands should call the transaction
/// overload so business mutation, audit and outbox commit together. The convenience overload exists
/// only for incremental migration of legacy endpoints that have already committed.
/// </summary>
public sealed class BusinessEventWriter(Database db, IntegrationOutbox outbox)
{
    public Task AccessChangedAsync(string username, string? actor = null, CancellationToken ct = default)
        => WriteLegacyAsync("identity.access.changed.v1", "identity.access.changed.v1", "access",
            $"user:{username}", actor, ct);

    public Task SessionRevokedAsync(string username, string sessionId, string? actor = null,
        CancellationToken ct = default)
        => WriteLegacyAsync("identity.session.revoked.v1", "identity.session.revoked.v1", "access",
            $"session:{sessionId}", actor, ct, username);

    public Task InvalidatedAsync(string scope, string? actor = null, CancellationToken ct = default)
        => WriteLegacyAsync("realtime.invalidate.v1", "legacy.realtime.invalidated.v1", scope,
            "all", actor, ct);

    public Task FeedbackResolvedAsync(string username, string? actor = null, CancellationToken ct = default)
        => WriteLegacyAsync("feedback.resolved.v1", "portal.feedback.resolved.v1", "feedback",
            $"user:{username}", actor, ct);

    public async Task<Guid> WriteAsync(NpgsqlConnection conn, NpgsqlTransaction tx,
        string eventType, string routingKey, string scope, string audience, string? actor,
        string? aggregateId = null, long? aggregateVersion = null, CancellationToken ct = default)
    {
        var data = JsonSerializer.SerializeToElement(new { scope }, IntegrationEventJson.Options);
        var envelope = new IntegrationEventEnvelope(Guid.NewGuid(), eventType, DateTimeOffset.UtcNow,
            "KetoanMini.Host", aggregateId, aggregateVersion, actor, null, null, [audience], data);
        var id = await outbox.EnqueueAsync(conn, tx, routingKey, envelope, ct);
        // Chuông đánh thức projector/publisher, gửi TRONG giao dịch nên chỉ tới nơi nếu lệnh ghi
        // commit thật. Sự kiện do lệnh gọi trực tiếp (không đi qua trigger bảng) nhờ đó cũng nhanh
        // như sự kiện của cầu nối, thay vì đợi hết nhịp poll.
        await conn.Cmd("SELECT pg_notify(@channel,@payload)", tx)
            .With("@channel", DatabaseChangePublisher.ChannelName)
            .With("@payload", scope)
            .ExecuteNonQueryAsync(ct);
        return id;
    }

    private async Task WriteLegacyAsync(string eventType, string routingKey, string scope,
        string audience, string? actor, CancellationToken ct, string? aggregateId = null)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await WriteAsync(conn, tx, eventType, routingKey, scope, audience, actor, aggregateId, null, ct);
        await tx.CommitAsync(ct);
    }
}
