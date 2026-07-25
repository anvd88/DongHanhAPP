using System.Collections.Concurrent;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Sổ theo dõi các KẾT NỐI đang mở tới <see cref="ChangesHub"/> (web VÀ app khi ở foreground) cùng
/// session_token (sid) tương ứng. Đây là phần cốt lõi thay cho "nhịp tim" HTTP mà client tự bắn lên để
/// báo còn sống: chính kết nối SignalR là bằng chứng "đang online", còn keep-alive ping sẵn có của
/// SignalR là nhịp tim ở tầng vận chuyển. <see cref="HubPresenceRefresher"/> đọc tập session_token ở
/// đây rồi làm tươi last_seen của user_sessions bằng MỘT lệnh gộp, để cửa sổ online 90 giây (xem các
/// truy vấn is_online trong ChatEndpoints/DirectoryEndpoints/UserEndpoints) vẫn đúng mà không cần
/// mỗi client bắn một request/45s.
///
/// LƯU Ý app native: SignalR của app CHỈ mở khi app ở foreground (tắt khi xuống nền để tiết kiệm pin).
/// Khi app ở nền, hiện diện do nhịp tim HTTP dự phòng của app lo — sổ này chỉ phản ánh foreground.
///
/// Chỉ giữ trong RAM của MỘT tiến trình — khớp triển khai hiện tại (Cloudflare Tunnel → một Kestrel).
/// Nhiều instance sẽ cần backplane; kể cả khi đó mỗi instance vẫn tự làm tươi phần kết nối của mình nên
/// trạng thái online vẫn đúng.
/// </summary>
public sealed class HubPresenceRegistry
{
    // connectionId → session_token (sid). Nhiều tab/kết nối cùng một phiên ⇒ nhiều connectionId trỏ về
    // CÙNG session_token; ANY(...) trong lệnh làm tươi coi trùng lặp là một nên không sao. Kết nối cuối
    // của một phiên đóng ⇒ không còn connectionId nào ⇒ ngừng làm tươi ⇒ last_seen tự cũ đi → offline.
    private readonly ConcurrentDictionary<string, string> _connections = new();

    public void Track(string connectionId, string sessionToken) => _connections[connectionId] = sessionToken;

    public void Drop(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>Tập session_token DUY NHẤT của các kết nối đang mở (đầu vào cho lệnh làm tươi gộp).</summary>
    public string[] ActiveSessionTokens()
        => _connections.Values.Distinct(StringComparer.Ordinal).ToArray();
}
