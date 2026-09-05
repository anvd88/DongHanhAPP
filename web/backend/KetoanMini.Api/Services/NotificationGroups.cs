namespace KetoanMini.Api.Services;

/// <summary>
/// Gom các "nhóm thông báo" mà mỗi người được tự tắt/bật ở Cài đặt.
///
/// Vì sao chốt ở MÁY CHỦ chứ không lọc trên giao diện: tắt một nhóm phải im cả chuông web LẪN rung
/// điện thoại. Lọc ở client thì thông báo vẫn được ghi, vẫn bắn FCM, chỉ là bị giấu đi — người dùng
/// vẫn bị đánh thức lúc nửa đêm. Chặn ngay tại <see cref="PushService"/> nên nhóm đã tắt thì không
/// sinh dòng hộp thư và cũng không gửi push.
///
/// CÓ NHỮNG NHÓM KHÔNG ĐƯỢC TẮT (trả về null ở <see cref="ForCategory"/>):
///   • security — "đăng nhập trên thiết bị mới": đây là cảnh báo an toàn tài khoản, tắt được thì kẻ
///     chiếm tài khoản chỉ cần tắt nó là người thật không bao giờ biết.
///   • system   — bản cập nhật app, thông báo vận hành.
/// </summary>
public static class NotificationGroups
{
    public const string Delivery = "delivery";
    public const string Collection = "collection";
    public const string Accounting = "accounting";
    public const string Work = "work";
    public const string People = "people";

    /// <summary>Thứ tự này cũng là thứ tự hiển thị trên trang Cài đặt.</summary>
    public static readonly string[] All = [Delivery, Collection, Accounting, Work, People];

    /// <summary>Nhãn tiếng Việt — chỉ để hiển thị.</summary>
    public static string Label(string group) => group switch
    {
        Delivery => "Giao hàng",
        Collection => "Thu tiền",
        Accounting => "Chứng từ & phiếu chi",
        Work => "Việc được giao & đơn từ",
        People => "Nhân sự & chấm công",
        _ => group,
    };

    /// <summary>
    /// Nhóm của một category thông báo. <c>null</c> = không thuộc nhóm nào tắt được, tức luôn gửi.
    /// </summary>
    public static string? ForCategory(string? category) => (category ?? "").Trim().ToLowerInvariant() switch
    {
        "delivery" => Delivery,
        "collection" => Collection,
        "document" or "payout" or "cashfund" => Accounting,
        "task" or "request" => Work,
        "penalty" or "attendance" => People,
        _ => null,
    };

    /// <summary>Khóa lưu trong <c>web_user_preferences</c>. Không có dòng = BẬT (mặc định nhận đủ).</summary>
    public static string PreferenceKey(string group) => $"notifyGroup.{group}";

    public static bool IsKnown(string? group) => group is not null && All.Contains(group);
}
