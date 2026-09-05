using System.Text.Json;

namespace KetoanMini.Api.Services;

/// <summary>
/// Thực thi các việc "push.*" trong hàng chờ bằng <see cref="PushService"/>.
/// </summary>
public sealed class PushOutboxHandler(PushService push, ILogger<PushOutboxHandler> log) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<bool> HandleAsync(OutboxMessage message, CancellationToken ct)
    {
        var job = JsonSerializer.Deserialize<PushService.PushJob>(message.Payload, Json);
        if (job is null)
        {
            // Payload hỏng thì thử lại bao nhiêu lần cũng vậy — coi như xong để khỏi kẹt hàng chờ,
            // nhưng log ở mức lỗi vì đây là thông báo không bao giờ tới nơi.
            log.LogError("Việc {Id} có payload không đọc được, bỏ qua: {Payload}", message.Id, message.Payload);
            return true;
        }

        if (!push.Enabled)
            throw new OutboxDeferredException(
                "FCM chưa cấu hình hoặc khởi tạo thất bại; giữ Pending để gửi lại khi hạ tầng sẵn sàng.");

        return message.Kind switch
        {
            OutboxQueue.KindUserPush => await push.DispatchUserAsync(job.Username, job.Title, job.Body, job.NotifId, job.Target),
            OutboxQueue.KindAdminsPush => await push.DispatchAdminsAsync(job.Title, job.Body, job.NotifId, job.Target),
            OutboxQueue.KindAllPush => await push.DispatchAllAsync(job.Title, job.Body, job.NotifId, job.Target),
            _ => UnknownKind(message),
        };
    }

    private bool UnknownKind(OutboxMessage message)
    {
        // Loại việc lạ (bản cũ/mới lẫn lộn lúc nâng cấp): đừng thử lại vô hạn, nhưng phải kêu.
        log.LogError("Việc {Id} có loại không hiểu được: {Kind}", message.Id, message.Kind);
        return true;
    }
}
