using KetoanMini.Api.Security;

namespace KetoanMini.Api.Services;

/// <summary>
/// BẢNG TIN ĐIỀU HÀNH — "ai vừa làm gì" gửi tới cấp quản lý trở lên, hiện ở chuông trên web.
///
/// Ai nhận: người giữ <see cref="Permissions.AttendanceRead"/> hoặc <see cref="Permissions.HrRead"/>,
/// cộng quản trị viên. Theo bảng vai trò hiện hành đó chính là Trưởng phòng, Nhân sự, Ban giám đốc
/// và Admin — đúng nghĩa "quản lý, quản lý nhân sự trở lên". Dùng QUYỀN chứ không liệt kê tên vai
/// trò để thêm vai trò mới sau này không phải sửa lại chỗ này.
///
/// CHỈ GHI CHUÔNG WEB, không bắn FCM (xem <see cref="PushService.SendWebOnlyToPermissionAsync"/>):
/// mỗi nhân viên chấm công 2 lần/ngày, một công ty 50 người là 100 tin — rung điện thoại từng ấy
/// lần thì quản lý sẽ tắt nhóm thông báo và mất luôn tin cần thiết.
///
/// Người tự gây ra sự kiện KHÔNG nhận tin của chính mình: một trưởng phòng vừa chấm công xong không
/// cần chuông báo là mình vừa chấm công.
///
/// Tắt/bật: nằm trong nhóm "Nhân sự &amp; chấm công" (chấm công) và "Việc được giao &amp; đơn từ"
/// (đơn từ) ở Cài đặt — xem <see cref="NotificationGroups"/>.
/// </summary>
public static class ManagementFeed
{
    /// <summary>Quyền nào thì được coi là "quản lý trở lên" cho bảng tin này.</summary>
    public static readonly string[] Audience = [Permissions.AttendanceRead, Permissions.HrRead];

    /// <summary>
    /// Một lượt chấm công vừa được ghi vào sổ. <paramref name="loai"/> là "Vào" hoặc "Ra".
    ///
    /// Lỗi ở đây KHÔNG được nổi lên: giờ công đã ghi xong rồi, không thể vì chuông hỏng mà trả về
    /// lỗi cho người vừa đứng trước camera.
    /// </summary>
    public static async Task AnnounceAttendanceAsync(PushService push, ILogger log,
        string username, string fullName, string loai, DateTime occurredAtUtc)
    {
        var who = string.IsNullOrWhiteSpace(fullName) ? username : fullName;
        var at = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc), Vietnam).ToString("HH:mm dd/MM");
        try
        {
            await push.SendWebOnlyToPermissionAsync(
                Audience,
                $"Chấm công {loai}: {who}",
                $"{who} vừa chấm công {loai} lúc {at}.",
                // Chữ ký gắn với ĐÚNG mốc phút: chấm lại trong cùng phút không đẻ thêm dòng, nhưng
                // giờ Ra cập nhật lúc khác vẫn là một tin mới.
                $"attn:{username.ToLowerInvariant()}:{loai}:{occurredAtUtc:yyyyMMddHHmm}",
                target: "Attendance",
                category: "attendance",
                exceptUsername: username);
        }
        catch (Exception ex)
        {
            log.LogWarning("Không gửi được tin chấm công tới quản lý: {Msg}", ex.Message);
        }
    }

    /// <summary>Một đơn từ vừa được nhân viên gửi lên.</summary>
    public static async Task AnnounceRequestAsync(PushService push, ILogger log,
        string requesterUsername, string requesterName, string requestNo, string typeLabel,
        Guid requestId, IReadOnlyCollection<string> alreadyNotified)
    {
        var who = string.IsNullOrWhiteSpace(requesterName) ? requesterUsername : requesterName;
        try
        {
            await push.SendWebOnlyToPermissionAsync(
                Audience,
                $"Đơn từ mới: {who}",
                $"{who} vừa gửi {typeLabel.ToLowerInvariant()} ({requestNo}).",
                $"reqnew:{requestId}",
                target: "Requests",
                category: "request",
                exceptUsername: requesterUsername,
                // Quản lý trực tiếp đã nhận "Đơn mới chờ duyệt" kèm push — đừng báo họ hai lần.
                skipUsernames: alreadyNotified);
        }
        catch (Exception ex)
        {
            log.LogWarning("Không gửi được tin đơn từ tới quản lý: {Msg}", ex.Message);
        }
    }

    private static readonly TimeZoneInfo Vietnam = LoadVietnam();

    private static TimeZoneInfo LoadVietnam()
    {
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Bangkok" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* nền tảng khác đặt tên khác */ }
        }
        return TimeZoneInfo.Local;
    }
}
