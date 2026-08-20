using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using KetoanMini.Api.Data;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace KetoanMini.Api.Services;

/// <summary>
/// Gửi thông báo đẩy tức thì tới điện thoại qua Firebase Cloud Messaging (FCM HTTP v1).
/// Token thiết bị lưu ở bảng <c>hr_device_tokens</c> (mỗi user có thể nhiều thiết bị).
/// Nếu chưa cấu hình <c>Firebase:CredentialsPath</c> thì service ở chế độ "tắt" (no-op) để API vẫn chạy.
/// </summary>
public sealed class PushService
{
    private readonly Database _db;
    private readonly ILogger<PushService> _log;
    private readonly OutboxQueue _outbox;
    private readonly bool _enabled;

    public PushService(IConfiguration config, Database db, OutboxQueue outbox, ILogger<PushService> log)
    {
        _db = db;
        _outbox = outbox;
        _log = log;
        var path = config["Firebase:CredentialsPath"];
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                if (FirebaseApp.DefaultInstance is null)
                    FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromFile(path) });
                _enabled = true;
                _log.LogInformation("PushService: Firebase Cloud Messaging đã sẵn sàng.");
            }
            else
            {
                _log.LogWarning("PushService: chưa cấu hình Firebase:CredentialsPath — bỏ qua push tức thì.");
            }
        }
        catch (Exception ex)
        {
            _enabled = false;
            _log.LogError(ex, "PushService: khởi tạo Firebase thất bại — bỏ qua push.");
        }
    }

    public bool Enabled => _enabled;

    /// <summary>Nội dung một thông báo đẩy đang nằm trong hàng chờ.</summary>
    internal sealed record PushJob(string? Username, string Title, string Body, string NotifId, string? Target);

    /// <summary>
    /// Đẩy tới mọi thiết bị của một user. <paramref name="notifId"/> là "chữ ký" ổn định của sự kiện
    /// (vd. <c>req:{id}:approved</c>, <c>inbox:{id}</c>, <c>pen:{id}</c>) — trùng với chữ ký của
    /// <c>NotificationCenter</c> trên app để CHỐNG TRÙNG với luồng kiểm tra nền.
    ///
    /// KHÔNG gọi FCM ngay: chỉ ghi một dòng vào hàng chờ rồi trả về, worker gửi sau (xem OutboxQueue).
    /// Nhờ vậy thao tác của người dùng không phải chờ mạng, và FCM lỗi thì việc vẫn được thử lại.
    /// </summary>
    public async Task SendToUserAsync(string? username, string title, string body, string notifId, string? target = null)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        await _outbox.EnqueueAsync(OutboxQueue.KindUserPush,
            new PushJob(username, title, body, notifId, target),
            DedupeKey(OutboxQueue.KindUserPush, username, notifId));
    }

    /// <summary>Xếp push trong cùng transaction với thay đổi nghiệp vụ nguồn.</summary>
    internal async Task<bool> EnqueueToUserAsync(NpgsqlConnection conn, NpgsqlTransaction tx,
        string username, string title, string body, string notifId, string? target = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        return await _outbox.EnqueueAsync(conn, tx, OutboxQueue.KindUserPush,
            new PushJob(username, title, body, notifId, target),
            DedupeKey(OutboxQueue.KindUserPush, username, notifId), ct);
    }

    /// <summary>
    /// Đẩy tới MỌI thiết bị đã đăng ký (thông báo chung như bản cập nhật app admin vừa phát hành).
    /// </summary>
    public async Task SendToAllAsync(string title, string body, string notifId, string? target = null)
    {
        await _outbox.EnqueueAsync(OutboxQueue.KindAllPush,
            new PushJob(null, title, body, notifId, target),
            DedupeKey(OutboxQueue.KindAllPush, null, notifId));
    }

    /// <summary>Đẩy tới mọi quản trị viên đang hoạt động (cho bước duyệt cấp Admin).</summary>
    public async Task SendToAdminsAsync(string title, string body, string notifId, string? target = null)
    {
        await _outbox.EnqueueAsync(OutboxQueue.KindAdminsPush,
            new PushJob(null, title, body, notifId, target),
            DedupeKey(OutboxQueue.KindAdminsPush, null, notifId));
    }

    /// <summary>
    /// Khoá khử trùng của một việc trong hàng chờ. PHẢI gồm NGƯỜI NHẬN chứ không chỉ chữ ký sự kiện:
    /// một tin nhắn chat gửi cho nhiều người dùng CHUNG notif_id (<c>chat:{conv}:{msg}</c>), nếu khoá
    /// chỉ có chữ ký thì chỉ người đầu tiên được xếp hàng, những người còn lại bị coi là trùng và MẤT
    /// thông báo. Chuẩn hoá chữ thường vì username so sánh không phân biệt hoa thường ở mọi nơi khác.
    /// </summary>
    internal static string DedupeKey(string kind, string? recipient, string notifId)
        => string.IsNullOrWhiteSpace(recipient)
            ? $"{kind}|{notifId}"
            : $"{kind}|{recipient.ToLowerInvariant()}|{notifId}";

    internal static string RecipientScope(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return "";
        var normalized = username.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    /// <summary>Gửi THẲNG, không qua hàng chờ — dùng cho việc đã tới hạn xử lý (worker) hoặc cuộc gọi.</summary>
    private async Task<bool> SendNowAsync(List<string> tokens, string title, string body, string notifId,
        string? target, string recipientScope)
    {
        if (!_enabled)
        {
            _log.LogWarning("FCM chưa sẵn sàng; giữ việc push {NotifId} trong outbox để retry.", notifId);
            return false;
        }
        if (tokens.Count == 0) return true;
        return await DispatchAsync(tokens, title, body, notifId, target, recipientScope);
    }

    internal async Task<bool> DispatchUserAsync(string? username, string title, string body, string notifId, string? target)
    {
        if (string.IsNullOrWhiteSpace(username)) return true;
        if (!_enabled) return await SendNowAsync([], title, body, notifId, target, RecipientScope(username));
        var tokens = await LoadTokensAsync(
            "SELECT token FROM hr_device_tokens WHERE lower(username)=lower(@u)", ("@u", username!));
        return await SendNowAsync(tokens, title, body, notifId, target, RecipientScope(username));
    }

    internal async Task<bool> DispatchAllAsync(string title, string body, string notifId, string? target)
    {
        if (!_enabled) return await SendNowAsync([], title, body, notifId, target, "");
        var tokens = await LoadTokensAsync("SELECT token FROM hr_device_tokens");
        return await SendNowAsync(tokens, title, body, notifId, target, "");
    }

    internal async Task<bool> DispatchAdminsAsync(string title, string body, string notifId, string? target)
    {
        if (!_enabled) return await SendNowAsync([], title, body, notifId, target, "");
        var tokens = await LoadTokensAsync("""
            SELECT dt.token FROM hr_device_tokens dt
            JOIN app_users u ON lower(u.username) = lower(dt.username)
            WHERE u.role = 'admin' AND u.is_active = TRUE AND COALESCE(u.is_deleted, FALSE) = FALSE
            """);
        return await SendNowAsync(tokens, title, body, notifId, target, "");
    }

    /// <summary>Đẩy tới nhân viên theo employeeId (tra username qua kết nối sẵn có).</summary>
    public async Task SendToEmployeeAsync(NpgsqlConnection conn, Guid employeeId, string title, string body, string notifId, string? target = null)
    {
        var username = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id")
            .With("@id", employeeId).ExecuteScalarAsync() as string;
        await SendToUserAsync(username, title, body, notifId, target);
    }

    /// <summary>
    /// Đẩy LỜI MỜI GỌI (thoại/video) tới mọi thiết bị của người nhận — để máy đổ chuông kể cả khi app
    /// đang ĐÓNG/nền (SignalR chỉ chạy khi app mở). Data-only, ưu tiên cao, TTL ngắn 30s để cuộc gọi
    /// nhỡ không "đổ chuông trễ". Bắt tay + media thật vẫn đi qua WebRTC (mã hóa DTLS-SRTP).
    /// </summary>
    public async Task SendCallInviteAsync(string? toUsername, string fromUsername, string callerName, string callId, string media)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(toUsername)) return;
        var tokens = await LoadTokensAsync(
            "SELECT token FROM hr_device_tokens WHERE lower(username)=lower(@u)", ("@u", toUsername!));
        await DispatchCallAsync(tokens, new Dictionary<string, string>
        {
            ["type"] = "call_invite",
            ["call_id"] = callId,
            // "from" là KHÓA BỊ CẤM trong data payload của FCM (bị từ chối INVALID_ARGUMENT) → dùng "caller".
            ["caller"] = fromUsername,
            ["caller_name"] = callerName,
            ["media"] = media,
            ["recipient_scope"] = RecipientScope(toUsername),
        });
    }

    /// <summary>Báo HỦY/nhỡ cuộc gọi để tắt chuông + thông báo ở máy người nhận (khi người gọi cúp trước).</summary>
    public async Task SendCallCancelAsync(string? toUsername, string fromUsername, string callId)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(toUsername)) return;
        var tokens = await LoadTokensAsync(
            "SELECT token FROM hr_device_tokens WHERE lower(username)=lower(@u)", ("@u", toUsername!));
        await DispatchCallAsync(tokens, new Dictionary<string, string>
        {
            ["type"] = "call_cancel",
            ["call_id"] = callId,
            ["caller"] = fromUsername, // "from" là khóa bị cấm trong FCM data payload
            ["recipient_scope"] = RecipientScope(toUsername),
        });
    }

    private async Task DispatchCallAsync(List<string> tokens, Dictionary<string, string> data)
    {
        if (tokens.Count == 0) return;
        for (var offset = 0; offset < tokens.Count; offset += 500)
        {
            var batch = tokens.GetRange(offset, Math.Min(500, tokens.Count - offset));
            try
            {
                var message = new MulticastMessage
                {
                    Tokens = batch,
                    Data = data,
                    // Ưu tiên cao + TTL ngắn: cuộc gọi cần tới ngay, và không còn ý nghĩa sau ~30s.
                    Android = new AndroidConfig { Priority = Priority.High, TimeToLive = TimeSpan.FromSeconds(30) },
                };
                var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
                if (response.FailureCount > 0) await PruneDeadTokensAsync(batch, response);
            }
            catch (Exception ex)
            {
                _log.LogWarning("PushService gửi FCM cuộc gọi lỗi: {Msg}", ex.Message);
            }
        }
    }

    private async Task<List<string>> LoadTokensAsync(string sql, params (string Name, object Value)[] ps)
    {
        var tokens = new List<string>();
        await using var conn = await _db.OpenAsync();
        var cmd = conn.Cmd(sql);
        foreach (var (name, value) in ps) cmd.With(name, value);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var t = r.Str(0);
            if (!string.IsNullOrWhiteSpace(t)) tokens.Add(t);
        }
        return tokens;
    }

    /// <summary>
    /// Gửi thật tới FCM. Trả false khi hỏng TẠM THỜI (mạng đứt, FCM lỗi) để worker thử lại — trước đây
    /// chỗ này nuốt lỗi, nên bên ngoài không thể biết thông báo có tới hay không.
    /// Token hỏng lẻ tẻ KHÔNG tính là hỏng: đó là thiết bị đã gỡ app, dọn đi là xong, thử lại vô ích.
    /// </summary>
    private async Task<bool> DispatchAsync(List<string> tokens, string title, string body, string notifId,
        string? target, string recipientScope)
    {
        if (tokens.Count == 0) return true;
        var ok = true;
        // FCM giới hạn 500 token/lần gửi multicast → chia lô để "gửi tới mọi thiết bị" vẫn an toàn.
        for (var offset = 0; offset < tokens.Count; offset += 500)
        {
            var batch = tokens.GetRange(offset, Math.Min(500, tokens.Count - offset));
            try
            {
                // Gửi DATA-ONLY (không có khối Notification): app luôn nhận ở onMessageReceived để tự dựng
                // thông báo + ghi nhận "chữ ký" (notif_id) → chống trùng với luồng kiểm tra nền (WorkManager).
                var message = new MulticastMessage
                {
                    Tokens = batch,
                    Data = new Dictionary<string, string>
                    {
                        ["title"] = title,
                        ["body"] = body,
                        ["notif_id"] = notifId,
                        ["notif_target"] = target ?? "",
                        ["recipient_scope"] = recipientScope,
                    },
                    Android = new AndroidConfig { Priority = Priority.High },
                };
                var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
                if (response.FailureCount > 0)
                {
                    await PruneDeadTokensAsync(batch, response);
                    // Unregistered là lỗi vĩnh viễn của riêng token và đã được dọn. Mọi lỗi còn lại
                    // có thể là FCM/mạng/payload tạm thời: giữ job pending để worker thử lại.
                    if (response.Responses.Any(r =>
                            !r.IsSuccess && r.Exception?.MessagingErrorCode is not MessagingErrorCode.Unregistered))
                        ok = false;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("PushService gửi FCM lỗi: {Msg}", ex.Message);
                ok = false;
            }
        }
        return ok;
    }

    /// <summary>Xóa các token không còn hợp lệ (đã gỡ app / hết hạn) để bảng gọn và không gửi thừa.</summary>
    private async Task PruneDeadTokensAsync(List<string> tokens, BatchResponse response)
    {
        var dead = new List<string>();
        for (var i = 0; i < response.Responses.Count; i++)
        {
            var r = response.Responses[i];
            if (r.IsSuccess) continue;
            var code = r.Exception?.MessagingErrorCode;
            // CHỈ xóa khi token thực sự không còn hợp lệ (Unregistered = app gỡ/hết hạn). KHÔNG xóa khi
            // InvalidArgument: lỗi đó thường do PAYLOAD sai (vd trước đây dùng khóa cấm "from") chứ không
            // phải token hỏng — xóa nhầm sẽ làm mất token tốt và không gọi được nữa.
            if (code is MessagingErrorCode.Unregistered)
                dead.Add(tokens[i]);
        }
        if (dead.Count == 0) return;
        try
        {
            await using var conn = await _db.OpenAsync();
            await conn.Cmd("DELETE FROM hr_device_tokens WHERE token = ANY(@t)")
                .With("@t", dead).ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _log.LogDebug("PushService dọn token lỗi: {Msg}", ex.Message);
        }
    }
}
