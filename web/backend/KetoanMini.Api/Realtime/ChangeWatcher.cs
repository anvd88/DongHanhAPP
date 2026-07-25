using System.Threading.Channels;
using KetoanMini.Api.Data;
using Microsoft.AspNetCore.SignalR;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Bridges PostgreSQL Pub/Sub (LISTEN/NOTIFY) to the existing SignalR protocol used by web and
/// desktop clients. This replaces the old 1.5-second whole-table checksum scan.
/// </summary>
public sealed class ChangeWatcher(
    IHubContext<ChangesHub> hub,
    Database db,
    ILogger<ChangeWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(100);
    // Phải khớp danh sách phạm vi trong DatabaseChangePublisher: payload lạ thì bỏ qua.
    private static readonly HashSet<string> AllowedScopes =
        new(["data", "presence", "hr", "tasks", "portal", "config", "audit", "talent",
             "release", "feedback", "attendance"],
            StringComparer.Ordinal);

    // Nhịp tim (45 giây/người) chỉ cập nhật last_seen của user_sessions nhưng vẫn kích hoạt trigger
    // 'presence'. Với N người dùng thì mỗi 45 giây có N thông báo, mỗi thông báo lại phát tới N máy →
    // N² tin nhắn. "presence" chỉ là gợi ý "dữ liệu đã cũ" nên gộp lại: phát nhiều nhất 1 lần/15 giây,
    // vẫn thừa nhanh so với ngưỡng offline 90 giây mà không phụ thuộc số người đang online.
    private static readonly Dictionary<string, TimeSpan> MinPublishInterval = new(StringComparer.Ordinal)
    {
        ["presence"] = TimeSpan.FromSeconds(15),
    };

    // Hàng chờ thông báo là tài nguyên CHUNG của cả cụm PostgreSQL (tối đa 8GB). Nếu có một phiên
    // LISTEN nào đó kẹt không đọc, hàng chờ dâng lên và tới lúc đầy thì MỌI giao dịch có gọi NOTIFY sẽ
    // LỖI NGAY LÚC COMMIT — tức người dùng không lưu được dữ liệu, chứ không chỉ mất realtime. Đây là
    // cái giá phải canh khi dồn mọi tín hiệu qua Pub/Sub, nên đo định kỳ để còn kịp xử lý trước khi vỡ.
    private static readonly TimeSpan QueueCheckInterval = TimeSpan.FromMinutes(1);
    private const double QueueWarnLevel = 0.25;

    // Đã từng LISTEN thành công chưa. Lần sau là NỐI LẠI → phải bảo máy khách nạp lại (xem ListenAsync).
    private bool _hasSubscribed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var triggersReady = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!triggersReady)
                {
                    var skipped = await DatabaseChangePublisher.EnsureAsync(db, stoppingToken);
                    triggersReady = true;
                    logger.LogInformation("Realtime database Pub/Sub triggers are ready.");

                    // Bảng có trong danh sách theo dõi nhưng CHƯA tồn tại thì bị bỏ qua. Không dừng
                    // hệ thống (một module tắt không được kéo sập realtime của module khác), nhưng
                    // phải kêu to: nếu im lặng thì màn hình của những bảng đó vĩnh viễn không tự cập
                    // nhật mà không ai biết. Thường là do thứ tự khởi động: EnsureTables của module
                    // chạy SAU khi trigger được cài, hoặc lần tạo bảng đó đã lỗi.
                    if (skipped.Count > 0)
                        logger.LogWarning(
                            "Realtime KHÔNG theo dõi được {Count} bảng vì chưa tồn tại: {Tables}. " +
                            "Màn hình liên quan sẽ không tự làm mới cho tới lần khởi động sau khi bảng " +
                            "đã được tạo.", skipped.Count, string.Join(", ", skipped));
                }

                await ListenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Realtime database listener disconnected: {Message}. Retrying.", ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        // Gom phạm vi đang chờ vào một TẬP HỢP thay vì hàng đợi chuỗi 64 ô (DropOldest). Hàng đợi cũ
        // xếp cả bản TRÙNG NHAU, nên trên lý thuyết một trận dội 'data' có thể đẩy văng thông báo 'hr'
        // đang xếp trước → máy khách nhân sự giữ dữ liệu cũ tới lần ghi sau. (Chưa dựng lại được cảnh
        // này trong test, xem RealtimeWatcherTests — đây là phòng xa chứ không phải vá lỗi đã gặp.)
        // Tập hợp thì trùng lặp tự tan: hàng chờ tối đa bằng SỐ PHẠM VI (8), không phụ thuộc lưu lượng.
        var pending = new HashSet<string>(StringComparer.Ordinal);
        // Kênh 1 ô chỉ đóng vai "có việc mới, dậy đi" — dữ liệu thật nằm ở `pending`.
        var wake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
        await using var conn = await db.OpenAsync(ct);

        conn.Notification += (_, notification) =>
        {
            if (!AllowedScopes.Contains(notification.Payload)) return;
            lock (pending) pending.Add(notification.Payload);
            wake.Writer.TryWrite(0);
        };

        await conn.Cmd($"LISTEN {DatabaseChangePublisher.ChannelName}").ExecuteNonQueryAsync(ct);
        logger.LogInformation("Realtime database listener subscribed to {Channel}.", DatabaseChangePublisher.ChannelName);

        // NỐI LẠI sau khi rớt: PostgreSQL KHÔNG giữ thông báo cho phiên đã ngắt, nên mọi thay đổi xảy
        // ra trong lúc gián đoạn đã mất hẳn. Máy khách vẫn đang nối SignalR bình thường nên tự chúng
        // không biết mà nạp lại — phải chủ động bảo nạp toàn bộ, nếu không chúng giữ dữ liệu cũ cho
        // tới lần ghi kế tiếp (có thể hàng giờ). Lần LISTEN đầu tiên thì bỏ qua: máy khách vừa kết nối
        // đã tự nạp dữ liệu rồi.
        if (_hasSubscribed)
        {
            logger.LogInformation("Realtime listener reconnected; asking clients to resync.");
            await hub.Clients.All.SendAsync("changed", "all", ct);
        }
        _hasSubscribed = true;

        using var listenerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = PumpNotificationsAsync(conn, wake.Writer, listenerCts.Token);
        var lastPublished = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var scheduledWakeAt = DateTime.MinValue;
        var lastQueueCheck = DateTime.UtcNow;
        try
        {
            while (await wake.Reader.WaitToReadAsync(ct))
            {
                while (wake.Reader.TryRead(out _)) { }

                // One business action often updates a row and appends an event in separate commits.
                // Keep pumping PostgreSQL while this tiny window coalesces those writes by scope.
                await Task.Delay(CoalesceWindow, ct);

                string[] scopes;
                lock (pending)
                {
                    scopes = [.. pending];
                    pending.Clear();
                }

                var now = DateTime.UtcNow;
                var retryAfter = TimeSpan.Zero;
                foreach (var pendingScope in scopes)
                {
                    // Phạm vi bị gộp nhịp mà chưa tới hạn thì TRẢ LẠI hàng chờ (không bỏ đi) rồi hẹn
                    // đánh thức đúng lúc hết hạn — máy khách vẫn được báo, chỉ muộn vài giây.
                    if (MinPublishInterval.TryGetValue(pendingScope, out var minInterval) &&
                        lastPublished.TryGetValue(pendingScope, out var previous) &&
                        now - previous < minInterval)
                    {
                        lock (pending) pending.Add(pendingScope);
                        var wait = minInterval - (now - previous);
                        if (wait > retryAfter) retryAfter = wait;
                        continue;
                    }

                    lastPublished[pendingScope] = now;
                    await hub.Clients.All.SendAsync("changed", pendingScope, ct);
                    logger.LogDebug("Realtime published scope {Scope}.", pendingScope);
                }

                if (now - lastQueueCheck >= QueueCheckInterval)
                {
                    lastQueueCheck = now;
                    await WarnIfNotifyQueueFillingAsync(ct);
                }

                // Mỗi nhịp tim đều đánh thức vòng này, nên chỉ đặt hẹn khi CHƯA có hẹn nào còn hiệu lực
                // (hoặc lần này tới hạn sớm hơn) — tránh đẻ hàng trăm hẹn giờ trùng nhau khi đông người.
                if (retryAfter > TimeSpan.Zero)
                {
                    var dueAt = now + retryAfter;
                    if (scheduledWakeAt <= now || dueAt < scheduledWakeAt)
                    {
                        scheduledWakeAt = dueAt;
                        ScheduleWake(wake.Writer, retryAfter, ct);
                    }
                }
            }

            await pump;
        }
        finally
        {
            listenerCts.Cancel();
            try { await pump; }
            catch (OperationCanceledException) when (listenerCts.IsCancellationRequested) { }
        }
    }

    /// <summary>
    /// Đo mức đầy của hàng chờ thông báo PostgreSQL. Bình thường phải xấp xỉ 0 — con số dâng lên nghĩa
    /// là có phiên LISTEN nào đó kẹt (có thể của ứng dụng khác trên cùng cụm), và nếu chạm 100% thì
    /// mọi lệnh ghi có trigger sẽ bắt đầu lỗi lúc commit. Dùng kết nối riêng vì kết nối đang LISTEN
    /// bận chờ ở WaitAsync.
    /// </summary>
    private async Task WarnIfNotifyQueueFillingAsync(CancellationToken ct)
    {
        try
        {
            await using var probe = await db.OpenAsync(ct);
            var usage = Convert.ToDouble(await probe.Cmd("SELECT pg_notification_queue_usage()").ExecuteScalarAsync(ct));
            if (usage >= QueueWarnLevel)
                logger.LogWarning(
                    "Hàng chờ thông báo PostgreSQL đã dùng {Usage:P0}. Nếu đầy, MỌI giao dịch có trigger " +
                    "realtime sẽ lỗi lúc commit. Kiểm tra phiên LISTEN nào đang kẹt không đọc " +
                    "(pg_stat_activity, cột wait_event).", usage);
        }
        catch (Exception ex)
        {
            // Đo được thì tốt, không đo được cũng không được làm sập vòng chính.
            logger.LogDebug("Không đo được mức hàng chờ thông báo: {Msg}", ex.Message);
        }
    }

    /// <summary>Hẹn đánh thức vòng đọc sau <paramref name="delay"/> để phát nốt phạm vi đang bị gộp nhịp.</summary>
    private static void ScheduleWake(ChannelWriter<byte> writer, TimeSpan delay, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, ct);
                writer.TryWrite(0);
            }
            catch (OperationCanceledException) { /* dừng dịch vụ / mất kết nối → thôi */ }
        }, CancellationToken.None);
    }

    private static async Task PumpNotificationsAsync(
        Npgsql.NpgsqlConnection conn,
        ChannelWriter<byte> writer,
        CancellationToken ct)
    {
        Exception? failure = null;
        try
        {
            while (!ct.IsCancellationRequested) await conn.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            writer.TryComplete(failure);
        }
    }
}
