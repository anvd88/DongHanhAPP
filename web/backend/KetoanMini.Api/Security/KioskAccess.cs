using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace KetoanMini.Api.Security;

/// <summary>
/// Gác các endpoint chấm công "ẩn danh" (kiosk web) khi hệ thống mở ra Internet qua tunnel.
/// Cho qua nếu MỘT trong các điều kiện đúng:
///   (a) request đã đăng nhập (APK / web có token),
///   (b) request đến từ máy nội bộ (loopback hoặc dải IP LAN riêng) — kiosk cắm thẳng trong mạng công ty,
///   (c) có header <c>X-Kiosk-Key</c> khớp <c>Security:KioskApiKey</c> (khóa cấp riêng cho thiết bị kiosk).
/// Chỉ SIẾT khi đã cấu hình <c>Security:KioskApiKey</c>; chưa cấu hình thì giữ nguyên hành vi cũ
/// (tương thích ngược để không làm gãy kiosk đang chạy) nhưng cảnh báo lúc khởi động.
/// </summary>
public static class KioskAccess
{
    public const string HeaderName = "X-Kiosk-Key";

    /// <summary>Đã cấu hình khóa kiosk ⇒ bật kiểm soát truy cập cho endpoint chấm công ẩn danh.</summary>
    public static bool IsEnforced(IConfiguration config)
        => !string.IsNullOrWhiteSpace(config["Security:KioskApiKey"]);

    public static bool IsAllowed(HttpContext ctx, IConfiguration config)
    {
        // (a) Đã đăng nhập hợp lệ (APK, web đã đăng nhập) → luôn cho qua.
        if (ctx.User.Identity?.IsAuthenticated == true) return true;

        // (b) Máy nội bộ: loopback hoặc IP LAN riêng. Sau UseForwardedHeaders, request qua Cloudflare
        // Tunnel mang IP CÔNG KHAI của khách (cloudflared là proxy loopback đã tin, X-Forwarded-For được
        // áp) nên KHÔNG lọt cửa này — chỉ kiosk cắm thẳng trong LAN mới được miễn khóa.
        if (config.GetValue("Security:KioskAllowLan", true))
        {
            var ip = ctx.Connection.RemoteIpAddress;
            if (ip is not null && (IPAddress.IsLoopback(ip) || IsPrivate(ip))) return true;
        }

        // (c) Khóa kiosk khớp (thiết bị kiosk được cấp khóa một lần, lưu cục bộ).
        var expected = config["Security:KioskApiKey"];
        var provided = ctx.Request.Headers[HeaderName].ToString();
        return !string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(provided)
            && FixedTimeEquals(expected, provided);
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (b.Length == 4)
        {
            return b[0] == 10                                   // 10.0.0.0/8
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16
                || (b[0] == 169 && b[1] == 254);                // 169.254.0.0/16 link-local
        }
        // IPv6: ULA fc00::/7 hoặc link-local fe80::/10.
        return (b[0] & 0xFE) == 0xFC || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}

/// <summary>
/// Endpoint filter áp cho <c>/api/chamcong/nhandien</c> và <c>/api/chamcong/cham</c>: trả 401 kèm mã
/// <c>kiosk_key_required</c> khi đã bật kiểm soát mà request không đủ điều kiện (để client hiện ô nhập mã kiosk).
/// </summary>
public sealed class KioskAccessFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var ctx = context.HttpContext;
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        if (KioskAccess.IsEnforced(config) && !KioskAccess.IsAllowed(ctx, config))
        {
            return Results.Json(
                new { code = "kiosk_key_required", message = "Thiết bị chưa được cấp quyền chấm công. Vui lòng nhập mã kiosk." },
                statusCode: StatusCodes.Status401Unauthorized);
        }
        return await next(context);
    }
}
