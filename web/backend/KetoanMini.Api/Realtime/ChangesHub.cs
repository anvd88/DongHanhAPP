using Microsoft.AspNetCore.SignalR;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Hub đẩy tín hiệu thay đổi dữ liệu xuống mọi client (web + desktop).
/// Broadcast "changed" không kèm dữ liệu nhạy cảm nên để mở (không yêu cầu đăng nhập) trong LAN.
/// Ngoài ra hub còn TRUNG CHUYỂN tín hiệu bắt tay WebRTC để gửi file thẳng P2P qua LAN.
/// </summary>
public sealed class ChangesHub : Hub
{
    /// <summary>
    /// Chuyển một gói tín hiệu WebRTC (lời mời gửi file / offer / answer / ICE / đồng ý / từ chối)
    /// từ người đang đăng nhập tới đúng MỘT người nhận. Đây CHỈ là tín hiệu bắt tay — nội dung tệp
    /// KHÔNG đi qua server mà truyền thẳng giữa 2 trình duyệt qua LAN. Kết nối ẩn danh (app desktop,
    /// không có UserIdentifier) bị bỏ qua để không lạm dụng kênh.
    /// </summary>
    public async Task Relay(string toUsername, string payload)
    {
        var from = Context.UserIdentifier;
        if (string.IsNullOrEmpty(from) || string.IsNullOrWhiteSpace(toUsername)) return;
        await Clients.User(toUsername).SendAsync("signal", from, payload);
    }
}
