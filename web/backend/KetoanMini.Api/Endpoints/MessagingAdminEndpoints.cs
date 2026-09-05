using System.Security.Claims;
using System.Text.Json;
using KetoanMini.Api.BuildingBlocks.Messaging;
using KetoanMini.Api.BuildingBlocks.Outbox;
using KetoanMini.Api.BuildingBlocks.Idempotency;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;

namespace KetoanMini.Api.Endpoints;

public static class MessagingAdminEndpoints
{
    public static void MapMessagingAdmin(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/messaging").RequirePermission(Permissions.UsersManage);
        group.MapGet("/dlq", async (Database db, CancellationToken ct) =>
        {
            await using var conn = await db.OpenAsync(ct);
            var rows = new List<object>();
            await using var reader = await conn.Cmd("""
                SELECT id,message_id,source_queue,routing_key,attempts,last_error,correlation_id,failed_at,replayed_at,replayed_by,version
                FROM messaging_dead_letters ORDER BY failed_at DESC LIMIT 200
                """).ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) rows.Add(new
            {
                id = reader.GetInt64(0), messageId = reader.GetGuid(1), sourceQueue = reader.GetString(2),
                routingKey = reader.GetString(3), attempts = reader.GetInt32(4), lastError = reader.GetString(5),
                correlationId = reader.IsDBNull(6) ? null : reader.GetString(6),
                failedAt = reader.GetFieldValue<DateTimeOffset>(7),
                replayedAt = reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(8),
                replayedBy = reader.IsDBNull(9) ? null : reader.GetString(9), version = reader.GetInt64(10),
            });
            return Results.Ok(rows);
        });

        group.MapGet("/dlq/{id:long}", async (long id, Database db, HttpContext http) =>
        {
            await using var conn = await db.OpenAsync(http.RequestAborted);
            await using var reader = await conn.Cmd("""
                SELECT message_id,source_queue,routing_key,attempts,last_error,correlation_id,
                       failed_at,replayed_at,replayed_by,version
                FROM messaging_dead_letters WHERE id=@id
                """).With("@id", id).ExecuteReaderAsync(http.RequestAborted);
            if (!await reader.ReadAsync(http.RequestAborted)) return Results.NotFound();
            var version = reader.GetInt64(9);
            http.Response.Headers.ETag = $"\"{version}\"";
            return Results.Ok(new
            {
                id, messageId = reader.GetGuid(0), sourceQueue = reader.GetString(1), routingKey = reader.GetString(2),
                attempts = reader.GetInt32(3), lastError = reader.GetString(4),
                correlationId = reader.IsDBNull(5) ? null : reader.GetString(5),
                failedAt = reader.GetFieldValue<DateTimeOffset>(6),
                replayedAt = reader.IsDBNull(7) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(7),
                replayedBy = reader.IsDBNull(8) ? null : reader.GetString(8), version,
            });
        });

        group.MapPost("/dlq/{id:long}/replay", async (long id, ClaimsPrincipal principal,
            Database db, IntegrationOutbox outbox, IdempotencyStore idempotency, HttpContext http) =>
        {
            var actor = principal.Username();
            var idempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return Results.Json(new { message = "Thiếu Idempotency-Key." }, statusCode: 428);
            var expectedVersion = ParseIfMatch(http.Request.Headers.IfMatch.FirstOrDefault());
            if (!expectedVersion.HasValue)
                return Results.Json(new { message = "Thiếu hoặc sai If-Match; hãy GET DLQ item để lấy ETag." }, statusCode: 428);
            await using var conn = await db.OpenAsync(http.RequestAborted);
            await using var tx = await conn.BeginTransactionAsync(http.RequestAborted);
            var lease = await idempotency.BeginAsync(conn, tx, actor, "messaging.dlq.replay", idempotencyKey,
                JsonSerializer.Serialize(new { id, expectedVersion }), http.RequestAborted);
            if (lease.Decision == IdempotencyDecision.Conflict)
                return Results.Conflict(new { message = "Idempotency-Key đã được dùng với payload khác hoặc command đang xử lý." });
            if (lease.Decision == IdempotencyDecision.Replay)
                return Results.Content(lease.ResponseBody ?? "{}", "application/json", statusCode: lease.ResponseStatus ?? 202);
            string route;
            string json;
            long currentVersion;
            await using (var reader = await conn.Cmd("""
                SELECT routing_key,envelope::text,version FROM messaging_dead_letters
                WHERE id=@id AND replayed_at IS NULL FOR UPDATE
                """, tx).With("@id", id).ExecuteReaderAsync(http.RequestAborted))
            {
                if (!await reader.ReadAsync(http.RequestAborted)) return Results.NotFound();
                route = reader.GetString(0); json = reader.GetString(1); currentVersion = reader.GetInt64(2);
            }
            if (currentVersion != expectedVersion.Value)
                return Results.Json(new { message = "Phiên bản DLQ đã thay đổi." }, statusCode: 412);
            var original = JsonSerializer.Deserialize<IntegrationEventEnvelope>(json, IntegrationEventJson.Options);
            if (original is null) return Results.Conflict(new { message = "DLQ envelope không hợp lệ." });
            var replay = original with
            {
                EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
                CausationId = original.EventId.ToString("D"), Actor = actor,
            };
            await outbox.EnqueueAsync(conn, tx, route, replay, http.RequestAborted);
            await conn.Cmd("""
                UPDATE messaging_dead_letters
                   SET replayed_at=CURRENT_TIMESTAMP,replayed_by=@actor,version=version+1
                 WHERE id=@id AND version=@version
                """, tx).With("@id", id).With("@actor", actor).With("@version", expectedVersion.Value)
                .ExecuteNonQueryAsync(http.RequestAborted);
            await conn.RecordAudit(tx, actor, "Replay DLQ", "MessagingDeadLetter", id.ToString(),
                $"Replay {original.EventId} as {replay.EventId} from {route}.", http.RequestAborted);
            var responseBody = JsonSerializer.Serialize(new { eventId = replay.EventId }, IntegrationEventJson.Options);
            await idempotency.CompleteAsync(conn, tx, actor, "messaging.dlq.replay", idempotencyKey,
                202, responseBody, http.RequestAborted);
            await tx.CommitAsync(http.RequestAborted);
            return Results.Content(responseBody, "application/json", statusCode: 202);
        });
    }

    private static long? ParseIfMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..];
        normalized = normalized.Trim('"');
        return long.TryParse(normalized, out var version) && version > 0 ? version : null;
    }
}
