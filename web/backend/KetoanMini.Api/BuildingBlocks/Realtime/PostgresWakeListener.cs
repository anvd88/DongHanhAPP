using KetoanMini.Api.Data;
using KetoanMini.Api.Realtime;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace KetoanMini.Api.BuildingBlocks.Realtime;

/// <summary>
/// Chuông báo "outbox vừa có việc". Người phát chỉ ĐÁNH chuông (không mang dữ liệu), người nhận vẫn
/// đọc từ PostgreSQL — mất một tiếng chuông chỉ làm chậm tới nhịp chờ kế tiếp, không mất sự kiện.
/// </summary>
public sealed class OutboxSignal
{
    private readonly Channel<byte> _channel = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    public void Signal() => _channel.Writer.TryWrite(0);

    /// <summary>Chờ tới khi có chuông hoặc hết <paramref name="fallback"/> (nhịp poll an toàn).</summary>
    public async Task WaitAsync(TimeSpan fallback, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(fallback);
        try { await _channel.Reader.ReadAsync(timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}

/// <summary>
/// Một kết nối PostgreSQL chuyên nghe LISTEN. Trigger cầu nối phát
/// <see cref="DatabaseChangePublisher.ChannelName"/> ngay khi giao dịch nghiệp vụ COMMIT, còn
/// projector phát <see cref="RealtimeWakeChannel"/> sau khi đã ghi realtime_events.
///
/// Vì sao cần: trước đây projector chỉ poll outbox mỗi giây và luồng SSE poll mỗi 2 giây, nên một
/// thao tác của người dùng mất 0,8–1,6 giây mới hiện trên máy khác — đo thật bằng curl. pg_notify đã
/// được trigger gửi từ trước nhưng KHÔNG AI NGHE, tức là một nửa cây cầu đã xây mà chưa nối.
/// Notify chỉ là gia tốc: nhịp poll cũ vẫn giữ nguyên làm lưới an toàn, và vì notification của
/// PostgreSQL chỉ tới sau COMMIT nên không thể đánh thức sớm hơn dữ liệu.
/// </summary>
public sealed class PostgresWakeListener(
    Database db,
    OutboxSignal outboxSignal,
    RealtimeWakeHub wake,
    IOptions<RealtimeOptions> realtime,
    ILogger<PostgresWakeListener> logger) : BackgroundService
{
    /// <summary>Kênh projector dùng để báo "realtime_events đã có dòng mới tới sequence N".</summary>
    public const string RealtimeWakeChannel = "ketoanmini_realtime_wake";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!realtime.Value.SseEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = await db.OpenAsync(stoppingToken);
                conn.Notification += OnNotification;
                try
                {
                    await conn.Cmd($"LISTEN {DatabaseChangePublisher.ChannelName}")
                        .ExecuteNonQueryAsync(stoppingToken);
                    await conn.Cmd($"LISTEN {RealtimeWakeChannel}").ExecuteNonQueryAsync(stoppingToken);
                    // Có thể đã có việc tồn từ trước khi kết nối này mở.
                    outboxSignal.Signal();
                    while (!stoppingToken.IsCancellationRequested)
                        await conn.WaitAsync(stoppingToken);
                }
                finally { conn.Notification -= OnNotification; }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "PostgreSQL LISTEN wake-up unavailable; realtime falls back to polling: {Message}",
                    ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void OnNotification(object sender, Npgsql.NpgsqlNotificationEventArgs e)
    {
        if (e.Channel == RealtimeWakeChannel)
        {
            if (long.TryParse(e.Payload, out var cursor)) wake.Publish(cursor);
            return;
        }
        outboxSignal.Signal();
    }
}
