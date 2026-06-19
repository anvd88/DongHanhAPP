using Microsoft.AspNetCore.SignalR;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Hub đẩy tín hiệu thay đổi dữ liệu xuống mọi client (web + desktop).
/// Không nhận lệnh từ client — chỉ phục vụ broadcast; không kèm dữ liệu nhạy cảm
/// (chỉ là tín hiệu "có thay đổi"), nên để mở (không yêu cầu đăng nhập) trong mạng LAN.
/// </summary>
public sealed class ChangesHub : Hub
{
}
