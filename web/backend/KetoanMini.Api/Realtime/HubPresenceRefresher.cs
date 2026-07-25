using KetoanMini.Api.Data;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Làm tươi last_seen cho các phiên đang giữ kết nối SignalR (web + app foreground) — thay cho nhịp tim
/// HTTP mà client tự bắn. MỘT lệnh UPDATE gộp cho TẤT CẢ người dùng mỗi <see cref="RefreshInterval"/>,
/// thay vì một request + một lệnh ghi cho MỖI người mỗi 45 giây (trước đây N người ⇒ N×N tin realtime).
///
/// Chu kỳ 45s &lt; cửa sổ online 90s: người đang giữ kết nối luôn nằm trong ngưỡng "đang online" với dư
/// 45s đề phòng trễ. Kết nối rớt ⇒ <see cref="HubPresenceRegistry"/> không còn token đó ⇒ ngừng làm
/// tươi ⇒ last_seen tự cũ đi và sau 90s hiện offline — đúng như khi nhịp tim cũ ngừng lại.
///
/// App native ở NỀN không có kết nối SignalR nên không nằm ở đây; nhịp tim HTTP dự phòng của app (chỉ
/// còn chạy khi SignalR tắt) mới giữ hiện diện lúc đó. Token nào không còn phiên sống (is_active=false
/// hoặc revoked) thì lệnh bỏ qua nên không "hồi sinh" phiên đã đăng xuất/thu hồi.
/// </summary>
public sealed class HubPresenceRefresher(
    HubPresenceRegistry registry,
    Database db,
    ILogger<HubPresenceRefresher> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(45);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(RefreshInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var tokens = registry.ActiveSessionTokens();
                if (tokens.Length == 0) continue;
                try
                {
                    await using var conn = await db.OpenAsync(stoppingToken);
                    await conn.Cmd(
                        @"UPDATE user_sessions
                             SET last_seen = CURRENT_TIMESTAMP
                           WHERE session_token = ANY(@tokens)
                             AND is_active = TRUE AND revoked = FALSE")
                        .With("@tokens", tokens)
                        .ExecuteNonQueryAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // DB chập chờn thì bỏ nhịp này; kết nối vẫn mở nên nhịp sau sẽ làm tươi lại.
                    logger.LogDebug("Không làm tươi được hiện diện qua hub: {Msg}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Dừng dịch vụ — thoát êm.
        }
    }
}
