namespace KetoanMini.Api.Services;

/// <summary>
/// Thực thi một việc trong hàng chờ. Tách ra thành giao diện để test thay được bằng bản giả — không
/// thì muốn thử đường hỏng/thử lại là phải chọc thật vào Firebase.
/// </summary>
public interface IOutboxHandler
{
    /// <summary>
    /// Trả true nếu đã xong (kể cả trường hợp "không có gì để làm", vd chưa cấu hình Firebase hoặc
    /// người nhận không có thiết bị nào). Trả false / ném lỗi khi HỎNG TẠM THỜI và nên thử lại.
    /// </summary>
    Task<bool> HandleAsync(OutboxMessage message, CancellationToken ct);
}

/// <summary>
/// Rút việc khỏi <see cref="OutboxQueue"/> và giao cho <see cref="IOutboxHandler"/> chạy.
/// Chạy ngoài request nên FCM chậm không còn làm chậm thao tác của người dùng.
/// </summary>
public sealed class OutboxWorker(
    OutboxQueue queue,
    IOutboxHandler handler,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    private const int BatchSize = 20;

    /// <summary>
    /// Lưới đỡ khi cú đánh thức trong tiến trình bị rơi, và là nhịp để việc ĐANG HẸN THỬ LẠI tới lượt.
    /// Bảng nhỏ + index riêng phần nên hỏi mỗi 5 giây gần như không tốn gì.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCleanup = DateTime.UtcNow + CleanupInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DrainAsync(stoppingToken);

                if (DateTime.UtcNow >= nextCleanup)
                {
                    var removed = await queue.CleanupAsync(stoppingToken);
                    nextCleanup = DateTime.UtcNow + CleanupInterval;
                    if (removed > 0) logger.LogInformation("Dọn {Count} việc đã xong khỏi hàng chờ.", removed);

                    // Nhắc lại việc đã bỏ hẳn: mỗi cái là một thông báo KHÔNG tới nơi. Nếu con số này
                    // lớn dần thì FCM/cấu hình đang hỏng chứ không phải trục trặc nhất thời.
                    var dead = await queue.DeadCountAsync(stoppingToken);
                    if (dead > 0)
                        logger.LogWarning(
                            "Hàng chờ còn {Count} việc đã bỏ hẳn — mỗi việc là một thông báo KHÔNG tới " +
                            "nơi. Xem bảng app_outbox (status='dead', cột last_error).", dead);
                }

                // Còn việc thì quay lại ngay; hết việc thì chờ tín hiệu đánh thức, tối đa PollInterval.
                if (processed > 0) continue;
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                idle.CancelAfter(PollInterval);
                try { await queue.Wake.ReadAsync(idle.Token); }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Hàng chờ không với tới được (DB đứt?) — đợi rồi thử lại, việc vẫn nằm nguyên trong bảng.
                logger.LogWarning("Vòng xử lý hàng chờ lỗi: {Msg}. Thử lại.", ex.Message);
                try { await Task.Delay(PollInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Xử lý hết các lô đang tới hạn. Trả về số việc đã chạm tới.</summary>
    private async Task<int> DrainAsync(CancellationToken ct)
    {
        var total = 0;
        while (!ct.IsCancellationRequested)
        {
            var batch = await queue.ClaimAsync(BatchSize, ct);
            if (batch.Count == 0) break;
            total += batch.Count;

            foreach (var message in batch)
            {
                try
                {
                    if (await handler.HandleAsync(message, ct))
                        await queue.CompleteAsync(message.Id, ct);
                    else
                        await queue.FailAsync(message.Id, message.Attempts, "Bên nhận báo chưa gửi được.", ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Đang tắt máy chủ: KHÔNG đánh dấu hỏng. Hết hạn thuê lượt việc tự quay lại hàng chờ.
                    throw;
                }
                catch (Exception ex)
                {
                    await queue.FailAsync(message.Id, message.Attempts, ex.Message, ct);
                }
            }
        }
        return total;
    }
}
