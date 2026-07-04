using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using KetoanMini.Api.Data;
using Npgsql;

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
    private readonly bool _enabled;

    public PushService(IConfiguration config, Database db, ILogger<PushService> log)
    {
        _db = db;
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

    /// <summary>
    /// Đẩy tới mọi thiết bị của một user. <paramref name="notifId"/> là "chữ ký" ổn định của sự kiện
    /// (vd. <c>req:{id}:approved</c>, <c>inbox:{id}</c>, <c>pen:{id}</c>) — trùng với chữ ký của
    /// <c>NotificationCenter</c> trên app để CHỐNG TRÙNG với luồng kiểm tra nền.
    /// </summary>
    public async Task SendToUserAsync(string? username, string title, string body, string notifId, string? target = null)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(username)) return;
        var tokens = await LoadTokensAsync(
            "SELECT token FROM hr_device_tokens WHERE lower(username)=lower(@u)", ("@u", username!));
        await DispatchAsync(tokens, title, body, notifId, target);
    }

    /// <summary>
    /// Đẩy tới MỌI thiết bị đã đăng ký (thông báo chung như bản cập nhật app admin vừa phát hành).
    /// </summary>
    public async Task SendToAllAsync(string title, string body, string notifId, string? target = null)
    {
        if (!_enabled) return;
        var tokens = await LoadTokensAsync("SELECT token FROM hr_device_tokens");
        await DispatchAsync(tokens, title, body, notifId, target);
    }

    /// <summary>Đẩy tới mọi quản trị viên đang hoạt động (cho bước duyệt cấp Admin).</summary>
    public async Task SendToAdminsAsync(string title, string body, string notifId, string? target = null)
    {
        if (!_enabled) return;
        var tokens = await LoadTokensAsync("""
            SELECT dt.token FROM hr_device_tokens dt
            JOIN app_users u ON lower(u.username) = lower(dt.username)
            WHERE u.role = 'admin' AND u.is_active = TRUE AND COALESCE(u.is_deleted, FALSE) = FALSE
            """);
        await DispatchAsync(tokens, title, body, notifId, target);
    }

    /// <summary>Đẩy tới nhân viên theo employeeId (tra username qua kết nối sẵn có).</summary>
    public async Task SendToEmployeeAsync(NpgsqlConnection conn, Guid employeeId, string title, string body, string notifId, string? target = null)
    {
        if (!_enabled) return;
        var username = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id")
            .With("@id", employeeId).ExecuteScalarAsync() as string;
        await SendToUserAsync(username, title, body, notifId, target);
    }

    private async Task<List<string>> LoadTokensAsync(string sql, params (string Name, object Value)[] ps)
    {
        var tokens = new List<string>();
        try
        {
            await using var conn = await _db.OpenAsync();
            var cmd = conn.Cmd(sql);
            foreach (var (name, value) in ps) cmd.With(name, value);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var t = r.Str(0);
                if (!string.IsNullOrWhiteSpace(t)) tokens.Add(t);
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug("PushService nạp token lỗi: {Msg}", ex.Message);
        }
        return tokens;
    }

    private async Task DispatchAsync(List<string> tokens, string title, string body, string notifId, string? target)
    {
        if (tokens.Count == 0) return;
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
                    },
                    Android = new AndroidConfig { Priority = Priority.High },
                };
                var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
                if (response.FailureCount > 0) await PruneDeadTokensAsync(batch, response);
            }
            catch (Exception ex)
            {
                _log.LogWarning("PushService gửi FCM lỗi: {Msg}", ex.Message);
            }
        }
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
            if (code is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
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
