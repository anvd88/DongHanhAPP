using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Định danh kết nối SignalR theo <c>username</c> (claim Name) thay vì NameIdentifier mặc định.
/// Nhờ vậy <c>Clients.Users(usernames)</c> nhắm đúng các thành viên cuộc trò chuyện.
/// Kết nối không có token (vd app desktop) trả null → không nhận tín hiệu user-targeted, vẫn
/// nhận broadcast data/presence bình thường.
/// </summary>
public sealed class NameUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => connection.User?.FindFirstValue(ClaimTypes.Name);
}
