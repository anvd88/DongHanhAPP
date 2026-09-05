namespace KetoanMini.Api.Realtime;

/// <summary>
/// Luật hiển thị "đang Online". Một tài khoản là Online khi còn phiên hoạt động có nhịp tim trong
/// <see cref="OnlineWindow"/> gần nhất.
///
/// Hằng số này phải DÙNG CHUNG ở cả ba nơi: hai truy vấn tính cờ is_online (/api/users,
/// /api/directory) và điều kiện WHEN của trigger user_sessions. Lệch nhau là hỏng ngầm theo hai
/// chiều: cửa sổ hiển thị NGẮN hơn ngưỡng trigger thì huy hiệu Online bật mà không máy nào được
/// báo; DÀI hơn thì máy khách bị đánh thức cho một thay đổi mà chính nó không hiển thị.
/// </summary>
public static class PresencePolicy
{
    /// <summary>Khoảng lặng tối đa vẫn còn được coi là đang online (dùng thẳng trong SQL).</summary>
    public const string OnlineWindow = "90 seconds";

    /// <summary>
    /// Bao lâu mới ghi lại last_seen một lần cho một phiên đang hoạt động. BẮT BUỘC ngắn hơn
    /// <see cref="OnlineWindow"/>. Trước đây ngưỡng này là 2 phút còn cửa sổ là 90 giây, nên một
    /// người đang ngồi làm việc trên web vẫn bị hiện Offline 30 giây trong mỗi 2 phút — và mỗi lần
    /// "sống lại" như thế lại đúng là một chuyển trạng thái thật, tức một lượt phát tín hiệu. Giữ
    /// ngưỡng ghi ngắn hơn cửa sổ thì người đang làm việc luôn hiện Online và KHÔNG sinh sự kiện nào.
    /// </summary>
    public const string TouchThrottle = "60 seconds";

    /// <summary>Biểu thức SQL "phiên này đang online" cho một bí danh bảng user_sessions.</summary>
    public static string IsOnlineSql(string sessionsAlias)
        => $"{sessionsAlias}.is_active = TRUE AND {sessionsAlias}.last_seen >= CURRENT_TIMESTAMP - INTERVAL '{OnlineWindow}'";
}
