using System.Security.Claims;
using System.Text.Json;
using KetoanMini.Api.Endpoints;
using Microsoft.Extensions.Options;

namespace KetoanMini.Api.BuildingBlocks.Realtime;

public static class RealtimeEndpoints
{
    public static IEndpointRouteBuilder MapBusinessRealtime(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/realtime/stream", StreamAsync)
            .RequireAuthorization()
            .DisableRequestTimeout();
        return endpoints;
    }

    private static async Task StreamAsync(
        HttpContext http,
        ClaimsPrincipal principal,
        RealtimeEventStore store,
        RealtimeWakeHub wake,
        RedisRealtimeCoordinator redis,
        IOptions<RealtimeOptions> configured)
    {
        var options = configured.Value;
        if (!options.SseEnabled)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var username = principal.Username();
        var sessionId = principal.FindFirstValue("sid") ?? "";
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(sessionId))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "text/event-stream; charset=utf-8";
        http.Response.Headers.CacheControl = "no-cache, no-store";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        http.Response.Headers["Content-Encoding"] = "identity";
        await http.Response.StartAsync(http.RequestAborted);

        var cursor = ParseCursor(http);
        var bounds = await store.BoundsAsync(http.RequestAborted);
        if (cursor is null)
        {
            cursor = bounds.Max;
            await WriteEventAsync(http, cursor.Value, "resync.required", "{\"scope\":\"all\",\"reason\":\"bootstrap\"}", true);
        }
        else if ((bounds.Min > 0 && cursor.Value < bounds.Min - 1) || cursor.Value > bounds.Max)
        {
            cursor = bounds.Max;
            await WriteEventAsync(http, cursor.Value, "resync.required", "{\"scope\":\"all\",\"reason\":\"cursor_expired\"}", true);
        }
        else
        {
            await http.Response.WriteAsync("retry: 3000\n\n", http.RequestAborted);
            await http.Response.Body.FlushAsync(http.RequestAborted);
        }

        // Chủ đề mà kết nối này đang cần. Máy khách gửi danh sách theo những màn hình nó đang mở;
        // không gửi gì thì nhận tất, giữ nguyên hành vi cho máy khách đời trước và cho APK.
        var topics = RealtimeEventStore.ParseTopics(http.Request.Query["topics"].FirstOrDefault());

        var connectionId = Guid.NewGuid().ToString("N");
        var nextHeartbeat = DateTimeOffset.UtcNow;
        var nextRevalidation = DateTimeOffset.UtcNow;
        var heartbeat = TimeSpan.FromSeconds(Math.Clamp(options.HeartbeatSeconds, 15, 20));
        var poll = TimeSpan.FromMilliseconds(Math.Clamp(options.PollMilliseconds, 500, 10000));
        using var subscription = wake.Subscribe();

        while (!http.RequestAborted.IsCancellationRequested)
        {
            var events = await store.ReadAsync(cursor.Value, username, sessionId, http.RequestAborted);
            foreach (var item in events)
            {
                // Mốc đọc tiến qua CẢ những khung bị bỏ, nếu không chúng nằm lại phía sau mốc và
                // được quét lại ở mọi vòng lặp cho tới khi hết hạn.
                if (RealtimeEventStore.ShouldDeliver(item.Scope, topics))
                    await WriteEventAsync(http, item.SequenceNo, item.EventType, item.Payload, false);
                cursor = item.SequenceNo;
            }
            if (events.Count > 0) continue;

            var now = DateTimeOffset.UtcNow;
            if (now >= nextRevalidation)
            {
                if (!await store.IsSessionAliveAsync(username, sessionId, http.RequestAborted))
                {
                    await WriteEventAsync(http, cursor.Value, "session.revoked",
                        "{\"scope\":\"access\"}", false);
                    break;
                }
                nextRevalidation = now.AddSeconds(30);
                await redis.TouchPresenceAsync(username, sessionId, connectionId);
            }
            if (now >= nextHeartbeat)
            {
                await http.Response.WriteAsync($": heartbeat {now:O}\n\n", http.RequestAborted);
                await http.Response.Body.FlushAsync(http.RequestAborted);
                nextHeartbeat = now.Add(heartbeat);
            }

            using var delay = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
            delay.CancelAfter(poll);
            try { await subscription.Reader.ReadAsync(delay.Token); }
            catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested) { }
        }
    }

    private static long? ParseCursor(HttpContext http)
    {
        var raw = http.Request.Headers["Last-Event-ID"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw)) raw = http.Request.Query["after"].FirstOrDefault();
        // Mốc 0 (hoặc không có mốc) = máy khách chưa từng nhận gì → bootstrap, KHÔNG phát lại từ dòng
        // đầu tiên. Không chặn ở đây thì một máy mới cài xin "after=0" bị dội về cả kho sự kiện còn
        // hạn (48 giờ), mà mỗi sự kiện là một lệnh tải lại màn hình.
        return long.TryParse(raw, out var value) && value > 0 ? value : null;
    }

    private static async Task WriteEventAsync(HttpContext http, long id, string eventType, string json, bool retry)
    {
        if (retry) await http.Response.WriteAsync("retry: 3000\n", http.RequestAborted);
        // Một ký tự xuống dòng lọt vào payload sẽ CẮT ĐÔI khung SSE và máy khách đọc ra rác. jsonb::text
        // của PostgreSQL hiện luôn cho một dòng, nhưng khung truyền không được phụ thuộc vào điều đó.
        var data = json.Replace("\r", "").Replace('\n', ' ');
        await http.Response.WriteAsync($"id: {id}\nevent: {eventType}\ndata: {data}\n\n", http.RequestAborted);
        await http.Response.Body.FlushAsync(http.RequestAborted);
    }
}

/// <summary>
/// Vòng dọn hạ tầng realtime. Ngoài realtime_events (48 giờ), nó còn dọn phần ĐÃ XONG của
/// integration_outbox/inbox_messages — trước đây hai bảng đó không ai dọn nên chỉ có thể phình mãi.
/// </summary>
public sealed class RealtimeRetentionWorker(
    RealtimeEventStore store,
    Outbox.IntegrationOutbox outbox,
    IOptions<MessagingRetentionOptions> retention,
    ILogger<RealtimeRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var keep = TimeSpan.FromDays(Math.Clamp(retention.Value.ProcessedRetentionDays, 1, 365));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = await store.CleanupAsync(stoppingToken);
                if (removed > 0) logger.LogInformation("Removed {Count} expired realtime events.", removed);
            }
            catch (Exception ex) { logger.LogWarning("Realtime retention sweep failed: {Message}", ex.Message); }
            try
            {
                var purged = await outbox.PurgeCompletedAsync(keep, stoppingToken);
                if (purged > 0)
                    logger.LogInformation("Purged {Count} processed outbox/inbox rows older than {Days} day(s).",
                        purged, keep.TotalDays);
            }
            catch (Exception ex) { logger.LogWarning("Messaging retention sweep failed: {Message}", ex.Message); }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
