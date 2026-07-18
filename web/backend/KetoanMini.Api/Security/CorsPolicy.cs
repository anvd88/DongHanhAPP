using System.Net;

namespace KetoanMini.Api.Security;

/// <summary>
/// Quyết định origin nào được phép gọi API qua CORS. Cho phép: (a) các origin liệt kê trong
/// <c>Cors:Origins</c> (domain thật, vd tunnel công khai), và (b) mọi origin cục bộ/LAN
/// (localhost/loopback/IP riêng) để chạy frontend dev qua LAN. Chặn mọi origin công khai lạ khác
/// (thay cho "cho phép tất cả" trước đây). Production phục vụ cùng origin nên request thật không đụng CORS.
/// </summary>
public static class CorsPolicy
{
    public static bool IsAllowed(string origin, string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;

        foreach (var a in allowed)
            if (string.Equals(a?.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                return true;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(host, out var ip))
            return IPAddress.IsLoopback(ip) || IsPrivate(ip);
        return false;
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (b.Length == 4)
        {
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }
        return (b[0] & 0xFE) == 0xFC || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80);
    }
}
