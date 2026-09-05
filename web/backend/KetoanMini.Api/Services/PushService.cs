using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
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
    public async Task SendToUserAsync(string? username, string title, string body, string notifId,
        string? target = null, string? link = null, string? category = null)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        if (await IsMutedAsync(username!, category ?? CategoryFor(target))) return;
        await RecordInboxAsync(username!, title, body, notifId, target, link, category);
        await _outbox.EnqueueAsync(OutboxQueue.KindUserPush,
            new PushJob(username, title, body, notifId, target),
            DedupeKey(OutboxQueue.KindUserPush, username, notifId));
    }

    /// <summary>
    /// Báo cho MỌI người đang giữ một trong các quyền — dùng cho sự kiện thuộc về cả một bộ phận
    /// ("tài xế vừa giao xong khách X" thì thủ kho, kế toán và quản trị viên đều cần biết), thay vì
    /// chỉ báo đúng một người như trước.
    ///
    /// <paramref name="exceptUsername"/> là CHÍNH người vừa gây ra sự kiện: không ai cần thông báo về
    /// việc mình vừa tự bấm.
    /// </summary>
    public async Task SendToPermissionAsync(IReadOnlyCollection<string> permissions, string title, string body,
        string notifId, string? target = null, string? link = null, string? category = null,
        bool includeAdmins = true, string? exceptUsername = null)
    {
        List<string> recipients;
        try
        {
            var wanted = includeAdmins ? [.. permissions, Permissions.UsersManage] : permissions.ToArray();
            recipients = await PermissionDirectory.UsersWithAnyPermissionAsync(_db, wanted);
        }
        catch (Exception ex)
        {
            // Không tra được danh sách người nhận thì thà mất một thông báo còn hơn làm hỏng nghiệp vụ
            // vừa ghi xong — nhưng phải kêu, vì im lặng ở đây nghĩa là cả phòng ban không hay biết gì.
            _log.LogError(ex, "Không tra được người nhận thông báo cho quyền {Perms}", string.Join(",", permissions));
            return;
        }

        var targets = recipients.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(r => string.IsNullOrWhiteSpace(exceptUsername)
                        || !string.Equals(r, exceptUsername, StringComparison.OrdinalIgnoreCase))
            .ToList();
        // Hỏi một lần cho cả danh sách thay vì mỗi người một truy vấn: một sự kiện giao hàng có thể
        // gửi cho cả chục người, và số đó còn tăng theo quy mô công ty.
        var muted = await MutedUsersAsync(targets, category ?? CategoryFor(target));

        foreach (var recipient in targets)
        {
            if (muted.Contains(recipient)) continue;
            await RecordInboxAsync(recipient, title, body, notifId, target, link, category);
            await _outbox.EnqueueAsync(OutboxQueue.KindUserPush,
                new PushJob(recipient, title, body, notifId, target),
                DedupeKey(OutboxQueue.KindUserPush, recipient, notifId));
        }
    }

    /// <summary>
    /// Chỉ ghi HỘP THƯ WEB cho những người đang giữ một trong các quyền — CỐ Ý KHÔNG bắn FCM.
    ///
    /// Dành cho tin "để mà biết" phát sinh liên tục trong ngày: ai vừa chấm công vào/ra, ai vừa gửi
    /// đơn từ. Quản lý cần thấy khi mở web, nhưng nếu rung điện thoại vài chục lần mỗi sáng thì thứ
    /// duy nhất xảy ra là họ tắt sạch nhóm thông báo — và mất luôn những tin thật sự cần.
    ///
    /// Vẫn đi qua đúng bộ lọc "nhóm thông báo đã tắt" như push thường, nên ai đã tắt nhóm Nhân sự &amp;
    /// chấm công thì cũng không có dòng nào trong chuông.
    /// </summary>
    /// <param name="skipUsernames">Người ĐÃ được báo bằng đường khác cho cùng sự kiện (vd. quản lý
    /// trực tiếp vừa nhận "Đơn mới chờ duyệt") — báo lần nữa chỉ là đếm hai lần.</param>
    /// <returns>Số người thật sự được ghi vào hộp thư.</returns>
    public async Task<int> SendWebOnlyToPermissionAsync(IReadOnlyCollection<string> permissions,
        string title, string body, string notifId, string? target = null, string? link = null,
        string? category = null, bool includeAdmins = true, string? exceptUsername = null,
        IReadOnlyCollection<string>? skipUsernames = null)
    {
        List<string> recipients;
        try
        {
            var wanted = includeAdmins ? [.. permissions, Permissions.UsersManage] : permissions.ToArray();
            recipients = await PermissionDirectory.UsersWithAnyPermissionAsync(_db, wanted);
        }
        catch (Exception ex)
        {
            // Mất một dòng thông báo còn hơn làm hỏng nghiệp vụ vừa ghi xong — nhưng phải kêu.
            _log.LogError(ex, "Không tra được người nhận thông báo cho quyền {Perms}", string.Join(",", permissions));
            return 0;
        }

        var skip = new HashSet<string>(skipUsernames ?? [], StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(exceptUsername)) skip.Add(exceptUsername!);

        var targets = recipients.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(r => !skip.Contains(r))
            .ToList();
        var muted = await MutedUsersAsync(targets, category ?? CategoryFor(target));

        var written = 0;
        foreach (var recipient in targets)
        {
            if (muted.Contains(recipient)) continue;
            await RecordInboxAsync(recipient, title, body, notifId, target, link, category);
            written++;
        }
        return written;
    }

    /* ---- Nhóm thông báo người dùng đã TẮT (xem NotificationGroups) ----
     * Không có dòng preference = BẬT. Chỉ dòng ghi rõ "false" mới là tắt, nên người chưa từng vào
     * Cài đặt vẫn nhận đủ mọi thông báo. */

    private async Task<bool> IsMutedAsync(string username, string? category)
    {
        var group = NotificationGroups.ForCategory(category);
        if (group is null) return false;
        var muted = await MutedUsersAsync([username], category);
        return muted.Contains(username);
    }

    private async Task<HashSet<string>> MutedUsersAsync(IReadOnlyCollection<string> usernames, string? category)
    {
        var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var group = NotificationGroups.ForCategory(category);
        if (group is null || usernames.Count == 0) return empty;
        try
        {
            await using var conn = await _db.OpenAsync();
            return await MutedUsersAsync(conn, null, usernames, group, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Không đọc được tuỳ chọn thì GỬI (mở mặc định): thà nhận thừa một thông báo còn hơn
            // lặng lẽ nuốt mất tin "tài xế đã giao hàng" vì cơ sở dữ liệu chập chờn.
            _log.LogWarning("Không đọc được tuỳ chọn thông báo: {Msg}", ex.Message);
            return empty;
        }
    }

    private static async Task<HashSet<string>> MutedUsersAsync(NpgsqlConnection conn, NpgsqlTransaction? tx,
        IReadOnlyCollection<string> usernames, string group, CancellationToken ct)
    {
        const string sql = """
            SELECT u.username
            FROM app_users u
            JOIN web_user_preferences p ON p.user_id = u.id
            WHERE p.preference_key = @k AND lower(p.preference_value) = 'false'
              AND lower(u.username) = ANY(@users)
            """;
        var cmd = (tx is null ? conn.Cmd(sql) : conn.Cmd(sql, tx))
            .With("@k", NotificationGroups.PreferenceKey(group))
            .With("@users", usernames.Select(u => u.Trim().ToLowerInvariant()).ToArray());
        var muted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) muted.Add(r.Str(0));
        return muted;
    }

    /// <summary>Xếp push trong cùng transaction với thay đổi nghiệp vụ nguồn.</summary>
    internal async Task<bool> EnqueueToUserAsync(NpgsqlConnection conn, NpgsqlTransaction tx,
        string username, string title, string body, string notifId, string? target = null,
        CancellationToken ct = default, string? link = null, string? category = null)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        if (NotificationGroups.ForCategory(category ?? CategoryFor(target)) is { } group)
        {
            // Dùng lại đúng kết nối/giao dịch đang mở: mở kết nối thứ hai giữa một giao dịch đang giữ
            // khoá hàng là cách nhanh nhất để tự khoá chính mình.
            var muted = await MutedUsersAsync(conn, tx, [username], group, ct);
            if (muted.Contains(username)) return false;
        }
        await RecordInboxAsync(conn, tx, username, title, body, notifId, target, link, category, ct);
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
    public async Task SendToAdminsAsync(string title, string body, string notifId, string? target = null,
        string? link = null, string? category = null)
    {
        await RecordInboxForAdminsAsync(title, body, notifId, target, link, category);
        await _outbox.EnqueueAsync(OutboxQueue.KindAdminsPush,
            new PushJob(null, title, body, notifId, target),
            DedupeKey(OutboxQueue.KindAdminsPush, null, notifId));
    }

    /* ------------------------------------------------------------------------------------------
     * HỘP THƯ THÔNG BÁO CỦA WEB (bảng web_notifications)
     *
     * App Android tự dựng thông báo từ gói FCM, nhưng TRÌNH DUYỆT thì không có FCM: đóng tab là mất.
     * Nên mỗi lần đẩy push, ghi luôn một dòng vào hộp thư — chuông trên header đọc từ đó. Đặt ở
     * ĐÂY (chứ không rải ra từng endpoint) vì PushService đã là cửa duy nhất mà mọi sự kiện đáng
     * báo phải đi qua: thêm nghiệp vụ mới chỉ cần gọi push như cũ là web có thông báo.
     *
     * Ghi hỏng KHÔNG được làm hỏng nghiệp vụ gốc: thông báo là hệ quả, không phải dữ liệu sổ sách.
     * ---------------------------------------------------------------------------------------- */

    private const string InboxInsertSql = """
        INSERT INTO web_notifications (username, title, body, category, link, app_target, notif_id, actor)
        VALUES (@u, @t, @b, @c, @l, @s, @n, @a)
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// Màn hình web tương ứng với "màn hình app" mà thông báo trỏ tới. Nhờ ánh xạ này, các nghiệp vụ
    /// đã có push từ trước tự có đường dẫn đúng trên web mà không phải sửa lại từng endpoint.
    /// </summary>
    internal static string WebLinkFor(string? target) => (target ?? "").Trim() switch
    {
        "Tasks" => "/cong-viec",
        "CashCollection" or "CashCollections" => "/lenh-thu-tien",
        "Approval" => "/pheduyet",
        "Requests" => "/dontu",
        "Penalty" => "/phat",
        "Attendance" => "/chamcong",
        "Settings" => "/caidat",
        "AppUpdate" => "/tai-apk",
        _ => "",
    };

    /// <summary>Nhóm thông báo — dùng để chọn biểu tượng/màu trên chuông, không chốt quyền gì cả.</summary>
    internal static string CategoryFor(string? target) => (target ?? "").Trim() switch
    {
        "Tasks" => "task",
        "CashCollection" or "CashCollections" => "collection",
        "Approval" or "Requests" => "request",
        "Penalty" => "penalty",
        "Attendance" => "attendance",
        "Settings" => "security",
        "AppUpdate" => "system",
        _ => "general",
    };

    private static NpgsqlCommand InboxCommand(NpgsqlCommand cmd, string username, string title, string body,
        string notifId, string? target, string? link, string? category)
        => cmd.With("@u", username.Trim())
              .With("@t", Clip(title, 200))
              .With("@b", Clip(body, 2000))
              .With("@c", category ?? CategoryFor(target))
              .With("@l", link ?? WebLinkFor(target))
              // Tên màn hình của APP (HrDestination) — cùng giá trị đang gửi trong gói FCM, để dòng
              // hộp thư mà app tải về bấm vào là đi đúng chỗ chứ không phải đoán từ đường dẫn web.
              .With("@s", AppTargetFor(target, category))
              .With("@n", notifId ?? "")
              .With("@a", "");

    /// <summary>
    /// Màn hình APP tương ứng. <paramref name="target"/> đã là tên màn của app ở hầu hết nghiệp vụ cũ;
    /// các nghiệp vụ mới (giao hàng, chứng từ…) chỉ có category nên suy ngược ra đây.
    /// Chuỗi rỗng = app không có màn tương ứng, bấm vào chỉ đánh dấu đã đọc.
    /// </summary>
    internal static string AppTargetFor(string? target, string? category)
    {
        var raw = (target ?? "").Trim();
        if (raw is "Tasks" or "Approval" or "Requests" or "Penalty" or "Attendance"
            or "Settings" or "AppUpdate" or "Payout" or "CashCollections") return raw;
        if (raw == "CashCollection") return "CashCollections";
        return (category ?? "").Trim().ToLowerInvariant() switch
        {
            "delivery" or "task" => "Tasks",
            "collection" => "CashCollections",
            "payout" => "Payout",
            "request" => "Requests",
            "penalty" => "Penalty",
            "attendance" => "Timesheet",
            // "document" cố ý để trống: app không có màn chứng từ nào để mở.
            _ => "",
        };
    }

    private static string Clip(string? value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    private async Task RecordInboxAsync(string username, string title, string body, string notifId,
        string? target, string? link, string? category)
    {
        try
        {
            await using var conn = await _db.OpenAsync();
            await InboxCommand(conn.Cmd(InboxInsertSql), username, title, body, notifId, target, link, category)
                .ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning("Không ghi được thông báo web cho {User}: {Msg}", username, ex.Message);
        }
    }

    /// <summary>
    /// Ghi trong CÙNG giao dịch nghiệp vụ: nghiệp vụ rollback thì thông báo cũng biến mất.
    ///
    /// Bọc trong SAVEPOINT vì ở PostgreSQL một lệnh lỗi làm HỎNG CẢ giao dịch — không có savepoint
    /// thì một sự cố của bảng thông báo (bảng chưa kịp tạo, đĩa đầy…) sẽ chặn luôn việc ghi lệnh thu
    /// tiền hay phiếu chi. Thông báo là hệ quả, tuyệt đối không được cản trở dữ liệu sổ sách.
    /// </summary>
    private async Task RecordInboxAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string username,
        string title, string body, string notifId, string? target, string? link, string? category,
        CancellationToken ct)
    {
        const string savepoint = "km_inbox";
        try
        {
            await tx.SaveAsync(savepoint, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Không đặt được savepoint cho thông báo web: {Msg}", ex.Message);
            return;
        }

        try
        {
            await InboxCommand(conn.Cmd(InboxInsertSql, tx), username, title, body, notifId, target, link, category)
                .ExecuteNonQueryAsync(ct);
            await tx.ReleaseAsync(savepoint, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ghi thông báo web trong giao dịch thất bại cho {User}", username);
            try { await tx.RollbackAsync(savepoint, ct); }
            catch (Exception rollback)
            {
                _log.LogError(rollback, "Không quay lui được savepoint thông báo web");
            }
        }
    }

    private async Task RecordInboxForAdminsAsync(string title, string body, string notifId,
        string? target, string? link, string? category)
    {
        try
        {
            var admins = new List<string>();
            await using var conn = await _db.OpenAsync();
            await using (var r = await conn.Cmd("""
                SELECT username FROM app_users
                WHERE lower(role) = 'admin' AND is_active = TRUE AND COALESCE(is_deleted, FALSE) = FALSE
                """).ExecuteReaderAsync())
            {
                while (await r.ReadAsync()) admins.Add(r.Str(0));
            }
            var muted = await MutedUsersAsync(admins, category ?? CategoryFor(target));
            foreach (var admin in admins)
            {
                if (muted.Contains(admin)) continue;
                await InboxCommand(conn.Cmd(InboxInsertSql), admin, title, body, notifId, target, link, category)
                    .ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning("Không ghi được thông báo web cho quản trị viên: {Msg}", ex.Message);
        }
    }

    /// <summary>
    /// Khoá khử trùng của một việc trong hàng chờ. PHẢI gồm NGƯỜI NHẬN chứ không chỉ chữ ký sự kiện:
    /// một sự kiện gửi cho nhiều người dùng có thể dùng CHUNG notif_id; nếu khoá chỉ có chữ ký thì
    /// chỉ người đầu tiên được xếp hàng, những người còn lại bị coi là trùng và MẤT thông báo.
    /// Chuẩn hoá chữ thường vì username so sánh không phân biệt hoa thường ở mọi nơi khác.
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

    /// <summary>Gửi THẲNG, không qua hàng chờ — dùng cho việc đã tới hạn xử lý bởi worker.</summary>
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

    /// <summary>
    /// So sánh vai trò KHÔNG phân biệt hoa/thường: app_users.role lưu đúng dạng "Admin" (xem
    /// <see cref="AppRoles"/>), nên câu cũ dùng 'admin' chữ thường không khớp dòng nào — thông báo
    /// gửi cho quản trị viên lặng lẽ rơi vào hư không.
    /// </summary>
    internal async Task<bool> DispatchAdminsAsync(string title, string body, string notifId, string? target)
    {
        if (!_enabled) return await SendNowAsync([], title, body, notifId, target, "");
        var tokens = await LoadTokensAsync("""
            SELECT dt.token FROM hr_device_tokens dt
            JOIN app_users u ON lower(u.username) = lower(dt.username)
            WHERE lower(u.role) = 'admin' AND u.is_active = TRUE AND COALESCE(u.is_deleted, FALSE) = FALSE
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
