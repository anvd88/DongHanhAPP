using System.Text.Json;
using System.Net.Http.Headers;
using KetoanMini.Api.BuildingBlocks.Idempotency;
using KetoanMini.Api.BuildingBlocks.Messaging;
using KetoanMini.Api.BuildingBlocks.Outbox;
using KetoanMini.Api.BuildingBlocks.Realtime;
using KetoanMini.Api.Data;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class MessagingInfrastructureTests(ApiFactory factory)
{
    [Fact]
    public async Task MandatoryAuditAndOutbox_CommitAndRollbackWithBusinessTransaction()
    {
        var db = factory.Services.GetRequiredService<Database>();
        var writer = factory.Services.GetRequiredService<BusinessEventWriter>();
        var committed = Guid.NewGuid();
        var rolledBack = Guid.NewGuid();
        await using (var conn = await db.OpenAsync())
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await conn.RecordAudit(tx, "tester", "Transactional test", "Test", committed.ToString(), "commit");
            await writer.WriteAsync(conn, tx, "test.committed.v1", "test.committed.v1", "data", "all",
                "tester", committed.ToString());
            await tx.CommitAsync();
        }
        await using (var conn = await db.OpenAsync())
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await conn.RecordAudit(tx, "tester", "Transactional test", "Test", rolledBack.ToString(), "rollback");
            await writer.WriteAsync(conn, tx, "test.rolledback.v1", "test.rolledback.v1", "data", "all",
                "tester", rolledBack.ToString());
            await tx.RollbackAsync();
        }
        await using var check = await db.OpenAsync();
        Assert.Equal(1, Convert.ToInt32(await check.Cmd(
            "SELECT COUNT(*) FROM audit_logs WHERE entity='Test' AND entity_name=@id")
            .With("@id", committed.ToString()).ExecuteScalarAsync()));
        Assert.Equal(0, Convert.ToInt32(await check.Cmd(
            "SELECT COUNT(*) FROM audit_logs WHERE entity='Test' AND entity_name=@id")
            .With("@id", rolledBack.ToString()).ExecuteScalarAsync()));
        Assert.Equal(1, Convert.ToInt32(await check.Cmd(
            "SELECT COUNT(*) FROM integration_outbox WHERE aggregate_id=@id")
            .With("@id", committed.ToString()).ExecuteScalarAsync()));
        Assert.Equal(0, Convert.ToInt32(await check.Cmd(
            "SELECT COUNT(*) FROM integration_outbox WHERE aggregate_id=@id")
            .With("@id", rolledBack.ToString()).ExecuteScalarAsync()));
        await check.Cmd("DELETE FROM audit_logs WHERE entity='Test' AND entity_name=@id")
            .With("@id", committed.ToString()).ExecuteNonQueryAsync();
        await check.Cmd("DELETE FROM integration_outbox WHERE aggregate_id=@id")
            .With("@id", committed.ToString()).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task RealtimeStore_ReplaysInOrder_AndDoesNotLeakTargetedEvents()
    {
        var store = factory.Services.GetRequiredService<RealtimeEventStore>();
        var db = factory.Services.GetRequiredService<Database>();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var after = (await store.BoundsAsync(default)).Max;
        await using (var conn = await db.OpenAsync())
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await store.AppendAsync(conn, tx, Envelope(ids[0], "data", "all"), default);
            await store.AppendAsync(conn, tx, Envelope(ids[1], "access", "user:alice"), default);
            await store.AppendAsync(conn, tx, Envelope(ids[2], "access", "user:bob"), default);
            await tx.CommitAsync();
        }

        var alice = (await store.ReadAsync(after, "alice", "session-a", default))
            .Where(x => ids.Contains(x.EventId)).ToArray();
        Assert.Equal([ids[0], ids[1]], alice.Select(x => x.EventId));
        Assert.True(alice[0].SequenceNo < alice[1].SequenceNo);

        await using var cleanup = await db.OpenAsync();
        await cleanup.Cmd("DELETE FROM realtime_events WHERE event_id=ANY(@ids)").With("@ids", ids)
            .ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task DuplicateEventId_ProducesOnlyOneRealtimeSideEffect()
    {
        var store = factory.Services.GetRequiredService<RealtimeEventStore>();
        var db = factory.Services.GetRequiredService<Database>();
        var envelope = Envelope(Guid.NewGuid(), "data", "all");
        Assert.NotNull(await AppendMaybeAsync(store, db, envelope));
        Assert.Null(await AppendMaybeAsync(store, db, envelope));

        await using var check = await db.OpenAsync();
        Assert.Equal(1, Convert.ToInt32(await check.Cmd(
            "SELECT COUNT(*) FROM realtime_events WHERE event_id=@id")
            .With("@id", envelope.EventId).ExecuteScalarAsync()));
        await check.Cmd("DELETE FROM realtime_events WHERE event_id=@id")
            .With("@id", envelope.EventId).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task SameIdempotencyKey_ReplaysResponse_AndDifferentPayloadConflicts()
    {
        var idempotency = factory.Services.GetRequiredService<IdempotencyStore>();
        var db = factory.Services.GetRequiredService<Database>();
        var key = Guid.NewGuid().ToString("N");
        await using (var conn = await db.OpenAsync())
        await using (var tx = await conn.BeginTransactionAsync())
        {
            var first = await idempotency.BeginAsync(conn, tx, "tester", "command.test", key, "{\"x\":1}");
            Assert.Equal(IdempotencyDecision.Execute, first.Decision);
            await idempotency.CompleteAsync(conn, tx, "tester", "command.test", key, 201, "{\"id\":1}");
            await tx.CommitAsync();
        }
        await using (var conn = await db.OpenAsync())
        await using (var tx = await conn.BeginTransactionAsync())
        {
            var replay = await idempotency.BeginAsync(conn, tx, "tester", "command.test", key, "{\"x\":1}");
            Assert.Equal(IdempotencyDecision.Replay, replay.Decision);
            Assert.Equal(201, replay.ResponseStatus);
            var conflict = await idempotency.BeginAsync(conn, tx, "tester", "command.test", key, "{\"x\":2}");
            Assert.Equal(IdempotencyDecision.Conflict, conflict.Decision);
            await tx.RollbackAsync();
        }
        await using var cleanup = await db.OpenAsync();
        await cleanup.Cmd("DELETE FROM api_idempotency WHERE username='tester' AND command_type='command.test' AND idempotency_key=@key")
            .With("@key", key).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ConcurrentSameIdempotencyKey_ExecutesOnlyOnce_AndReplaysTheCommittedResponse()
    {
        var idempotency = factory.Services.GetRequiredService<IdempotencyStore>();
        var db = factory.Services.GetRequiredService<Database>();
        var key = Guid.NewGuid().ToString("N");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<IdempotencyDecision> RunAsync(bool owner)
        {
            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            if (!owner) await ready.Task;
            var lease = await idempotency.BeginAsync(conn, tx, "tester", "command.concurrent", key, "{\"x\":1}");
            if (lease.Decision == IdempotencyDecision.Execute)
            {
                ready.TrySetResult();
                await Task.Delay(100);
                await idempotency.CompleteAsync(conn, tx, "tester", "command.concurrent", key, 201, "{\"ok\":true}");
                await tx.CommitAsync();
            }
            else await tx.RollbackAsync();
            return lease.Decision;
        }

        var owner = RunAsync(true);
        var contender = RunAsync(false);
        var decisions = await Task.WhenAll(owner, contender);
        Assert.Equal(1, decisions.Count(x => x == IdempotencyDecision.Execute));
        Assert.Equal(1, decisions.Count(x => x == IdempotencyDecision.Replay));

        await using var cleanup = await db.OpenAsync();
        await cleanup.Cmd("DELETE FROM api_idempotency WHERE username='tester' AND command_type='command.concurrent' AND idempotency_key=@key")
            .With("@key", key).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task SseStreamsNewEvent_AndReconnectReplaysMissedEventsInOrder()
    {
        const string sid = "messaging-sse-test";
        var token = await factory.EmployeeTokenAsync(sid);
        var db = factory.Services.GetRequiredService<Database>();
        var store = factory.Services.GetRequiredService<RealtimeEventStore>();
        var wake = factory.Services.GetRequiredService<RealtimeWakeHub>();
        await using (var seed = await db.OpenAsync())
            await seed.Cmd("""
                INSERT INTO user_sessions(session_token,username,machine_name,is_active,client_kind,revoked)
                VALUES (@sid,@username,'SSE test',TRUE,'Web',FALSE)
                ON CONFLICT(session_token) DO UPDATE SET username=EXCLUDED.username,is_active=TRUE,revoked=FALSE,last_seen=CURRENT_TIMESTAMP
                """).With("@sid", sid).With("@username", factory.EmpUser).ExecuteNonQueryAsync();

        var after = (await store.BoundsAsync(default)).Max;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            $"/api/realtime/stream?after={after}"), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);

        var firstId = Guid.NewGuid();
        var firstCursor = await AppendAsync(store, db, Envelope(firstId, "hr", "all"));
        wake.Publish(firstCursor);
        var live = await ReadEventsAsync(reader, 1, timeout.Token, new HashSet<long> { firstCursor });
        Assert.Equal((firstCursor, "invalidated"), live[0]);

        response.Dispose();
        var secondCursor = await AppendAsync(store, db, Envelope(Guid.NewGuid(), "tasks", "all"));
        var thirdCursor = await AppendAsync(store, db, Envelope(Guid.NewGuid(), "portal", "all"));
        using var replayResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            $"/api/realtime/stream?after={firstCursor}"), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var replayStream = await replayResponse.Content.ReadAsStreamAsync(timeout.Token);
        using var replayReader = new StreamReader(replayStream);
        var replayed = await ReadEventsAsync(replayReader, 2, timeout.Token,
            new HashSet<long> { secondCursor, thirdCursor });
        Assert.Equal([(secondCursor, "invalidated"), (thirdCursor, "invalidated")], replayed);
        timeout.Cancel();

        await using var cleanup = await db.OpenAsync();
        await cleanup.Cmd("DELETE FROM realtime_events WHERE sequence_no=ANY(@ids)")
            .With("@ids", new[] { firstCursor, secondCursor, thirdCursor }).ExecuteNonQueryAsync();
        await cleanup.Cmd("DELETE FROM user_sessions WHERE session_token=@sid").With("@sid", sid)
            .ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task TwoConditionalUpdatesWithSameVersion_OnlyOneSucceeds()
    {
        var db = factory.Services.GetRequiredService<Database>();
        var messageId = Guid.NewGuid();
        long id;
        await using (var seed = await db.OpenAsync())
            id = Convert.ToInt64(await seed.Cmd("""
                INSERT INTO messaging_dead_letters
                    (message_id,source_queue,routing_key,attempts,last_error,envelope)
                VALUES (@message,'test.q','test.v1',1,'test','{}'::jsonb) RETURNING id
                """).With("@message", messageId).ExecuteScalarAsync());

        async Task<int> TryUpdate(string actor)
        {
            await using var conn = await db.OpenAsync();
            return await conn.Cmd("""
                UPDATE messaging_dead_letters SET replayed_by=@actor,version=version+1
                WHERE id=@id AND version=1
                """).With("@actor", actor).With("@id", id).ExecuteNonQueryAsync();
        }
        var results = await Task.WhenAll(TryUpdate("one"), TryUpdate("two"));
        Assert.Equal(1, results.Sum());

        await using var cleanup = await db.OpenAsync();
        await cleanup.Cmd("DELETE FROM messaging_dead_letters WHERE id=@id").With("@id", id)
            .ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task RabbitEnabledPushQueue_WritesVersionedIntegrationEvent_AndDeduplicates()
    {
        var db = factory.Services.GetRequiredService<Database>();
        var integration = factory.Services.GetRequiredService<IntegrationOutbox>();
        var queue = new OutboxQueue(db, NullLogger<OutboxQueue>.Instance, integration,
            Options.Create(new RabbitMqOptions { Enabled = true }));
        var dedupe = "push.user|tester|" + Guid.NewGuid().ToString("N");
        var job = new PushService.PushJob("tester", "Title", "Body", "notification-id", "Requests");

        await using (var conn = await db.OpenAsync())
        await using (var tx = await conn.BeginTransactionAsync())
        {
            for (var i = 0; i < 2; i++)
                await queue.EnqueueAsync(conn, tx, OutboxQueue.KindUserPush, job, dedupe);
            await conn.Cmd("UPDATE integration_outbox SET available_at=CURRENT_TIMESTAMP+INTERVAL '1 hour' WHERE aggregate_id=@dedupe", tx)
                .With("@dedupe", dedupe).ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        await using var check = await db.OpenAsync();
        Assert.Equal(1, Convert.ToInt32(await check.Cmd("""
            SELECT COUNT(*) FROM integration_outbox
            WHERE event_type='notifications.push.requested.v1' AND aggregate_id=@dedupe
            """).With("@dedupe", dedupe).ExecuteScalarAsync()));
        await check.Cmd("DELETE FROM integration_outbox WHERE aggregate_id=@dedupe")
            .With("@dedupe", dedupe).ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Một dòng outbox hỏng KHÔNG được phép chặn dòng lành phía sau. Trước bản vá, projector ném lỗi
    /// ra tận vòng ngoài nên cả lô bị bỏ dở; lô luôn lấy dòng cũ nhất trước, nên đúng một dòng rác đủ
    /// để đóng băng toàn bộ realtime (đo thật: 8 dòng chặn 392 sự kiện suốt gần ba ngày).
    /// </summary>
    [Fact]
    public async Task PoisonOutboxRow_DoesNotBlockTheEventsBehindIt()
    {
        var db = factory.Services.GetRequiredService<Database>();
        var poison = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        var marker = $"poison-probe-{healthy:N}";
        await using (var seed = await db.OpenAsync())
        {
            // occurred_at lùi lại: dòng hỏng chắc chắn được claim TRƯỚC dòng lành.
            await seed.Cmd("""
                INSERT INTO integration_outbox (id,event_type,routing_key,payload,occurred_at)
                VALUES (@id,'test.poison.v1','test.poison.v1',@payload::jsonb,
                        CURRENT_TIMESTAMP - INTERVAL '1 minute')
                """).With("@id", poison).With("@payload", $$"""{"eventId":"{{poison}}"}""")
                .ExecuteNonQueryAsync();
            await using var tx = await seed.BeginTransactionAsync();
            await factory.Services.GetRequiredService<BusinessEventWriter>().WriteAsync(
                seed, tx, "test.healthy.v1", "test.healthy.v1", "data", "all", "tester", marker);
            await tx.CommitAsync();
        }

        var projected = false;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && !projected)
        {
            await using var check = await db.OpenAsync();
            projected = await check.Cmd(
                "SELECT 1 FROM integration_outbox WHERE aggregate_id=@id AND published_at IS NOT NULL")
                .With("@id", marker).ExecuteScalarAsync() is not null and not DBNull;
            if (!projected) await Task.Delay(500);
        }

        await using var cleanup = await db.OpenAsync();
        await cleanup.Cmd("DELETE FROM integration_outbox WHERE id=@id OR aggregate_id=@marker")
            .With("@id", poison).With("@marker", marker).ExecuteNonQueryAsync();
        await cleanup.Cmd("DELETE FROM messaging_dead_letters WHERE message_id=@id")
            .With("@id", poison).ExecuteNonQueryAsync();
        Assert.True(projected, "Dòng outbox hợp lệ phải được chiếu dù phía trước nó có một dòng hỏng.");
    }

    /// <summary>
    /// user_sessions là bảng bị ghi nhiều nhất hệ thống: mỗi máy đang mở làm tươi last_seen liên tục.
    /// Nhịp giữ phiên KHÔNG được phát tín hiệu (nếu không, tải tăng theo bình phương số người dùng
    /// để báo một tin không ai cần), nhưng những chuyển trạng thái THẬT thì phải phát.
    /// </summary>
    [Fact]
    public async Task SessionKeepAlive_IsSilent_WhileRealPresenceTransitionsStillPublish()
    {
        await factory.EmployeeTokenAsync();          // đảm bảo tài khoản test tồn tại
        var db = factory.Services.GetRequiredService<Database>();
        await DatabaseChangePublisher.EnsureAsync(db, [("user_sessions", ["presence"])]);
        var sid = $"presence-probe-{Guid.NewGuid():N}";
        await using var conn = await db.OpenAsync();

        async Task<int> PublishedByAsync(string sql)
        {
            var before = Convert.ToInt64(await conn.Cmd(
                "SELECT COUNT(*) FROM integration_outbox WHERE aggregate_type='user_sessions'")
                .ExecuteScalarAsync());
            await conn.Cmd(sql).With("@sid", sid).With("@u", factory.EmpUser).ExecuteNonQueryAsync();
            var after = Convert.ToInt64(await conn.Cmd(
                "SELECT COUNT(*) FROM integration_outbox WHERE aggregate_type='user_sessions'")
                .ExecuteScalarAsync());
            return (int)(after - before);
        }

        try
        {
            Assert.Equal(1, await PublishedByAsync("""
                INSERT INTO user_sessions(session_token,username,machine_name,is_active,client_kind,revoked,last_seen)
                VALUES (@sid,@u,'Presence probe',TRUE,'Web',FALSE,CURRENT_TIMESTAMP - INTERVAL '2 minutes')
                """));
            // Nhịp giữ phiên: last_seen nhích lên trong phạm vi cửa sổ Online → im lặng.
            for (var i = 0; i < 3; i++)
                Assert.Equal(0, await PublishedByAsync($"""
                    UPDATE user_sessions SET last_seen=CURRENT_TIMESTAMP - INTERVAL '{90 - 30 * i} seconds'
                    WHERE session_token=@sid
                    """));
            // Online TẮT vì im lặng: không có lệnh ghi nào, nên cũng không có tín hiệu nào (máy khách
            // tự làm mới chậm — xem presencePollMs trong useApi.ts).
            Assert.Equal(0, await PublishedByAsync("""
                UPDATE user_sessions SET last_seen=CURRENT_TIMESTAMP - INTERVAL '10 minutes'
                WHERE session_token=@sid
                """));
            // Quay lại sau khi đã hiện Offline = chuyển trạng thái thật.
            Assert.Equal(1, await PublishedByAsync(
                "UPDATE user_sessions SET last_seen=CURRENT_TIMESTAMP WHERE session_token=@sid"));
            Assert.Equal(1, await PublishedByAsync(
                "UPDATE user_sessions SET revoked=TRUE WHERE session_token=@sid"));
            Assert.Equal(1, await PublishedByAsync("DELETE FROM user_sessions WHERE session_token=@sid"));
        }
        finally
        {
            await conn.Cmd("DELETE FROM user_sessions WHERE session_token=@sid").With("@sid", sid)
                .ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM integration_outbox WHERE aggregate_type='user_sessions' AND published_at IS NULL")
                .ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Phong bì sinh bởi trigger mang mốc thời gian kết xuất theo TimeZone của KẾT NỐI đã ghi. Một
    /// kết nối không đặt UTC (psql, pgAdmin, script bảo trì) đẻ ra mốc +07:00, mà Npgsql chỉ nhận
    /// DateTimeOffset lệch 0 cho timestamptz — trước bản vá, sự kiện đó không bao giờ chiếu được.
    /// </summary>
    [Fact]
    public async Task EnvelopeWithNonUtcOffset_IsStillProjected()
    {
        var db = factory.Services.GetRequiredService<Database>();
        var store = factory.Services.GetRequiredService<RealtimeEventStore>();
        var id = Guid.NewGuid();
        var envelope = Envelope(id, "presence", "all") with
        {
            OccurredAt = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)),
        };
        var cursor = await AppendMaybeAsync(store, db, envelope);
        Assert.NotNull(cursor);

        await using var cleanup = await db.OpenAsync();
        await cleanup.Cmd("DELETE FROM realtime_events WHERE event_id=@id").With("@id", id)
            .ExecuteNonQueryAsync();
    }

    private static IntegrationEventEnvelope Envelope(Guid id, string scope, string audience) => new(
        id, scope == "access" ? "identity.access.changed.v1" : "realtime.invalidate.v1",
        DateTimeOffset.UtcNow, "tests", null, null, "tester", null, null, [audience],
        JsonSerializer.SerializeToElement(new { scope }));

    private static async Task<long> AppendAsync(
        RealtimeEventStore store, Database db, IntegrationEventEnvelope envelope)
    {
        return Assert.IsType<long>(await AppendMaybeAsync(store, db, envelope));
    }

    private static async Task<long?> AppendMaybeAsync(
        RealtimeEventStore store, Database db, IntegrationEventEnvelope envelope)
    {
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var cursor = await store.AppendAsync(conn, tx, envelope, default);
        await tx.CommitAsync();
        return cursor;
    }

    private static async Task<List<(long Id, string Event)>> ReadEventsAsync(
        StreamReader reader, int count, CancellationToken ct, IReadOnlySet<long>? onlyIds = null)
    {
        var result = new List<(long, string)>();
        long? id = null;
        string? eventType = null;
        while (result.Count < count)
        {
            var line = await reader.ReadLineAsync(ct) ?? throw new EndOfStreamException();
            if (line.Length == 0)
            {
                if (id.HasValue && eventType is not null && (onlyIds is null || onlyIds.Contains(id.Value)))
                    result.Add((id.Value, eventType));
                id = null; eventType = null;
            }
            else if (line.StartsWith("id: ", StringComparison.Ordinal))
                id = long.Parse(line[4..]);
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
                eventType = line[7..];
        }
        return result;
    }
}
