using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace KetoanMini.Api.Security;

/// <summary>
/// Vé tiền xác thực cho trang đăng nhập web. Vé chứng minh trình duyệt vừa khởi tạo một phiên đăng
/// nhập với chính máy chủ này, được ràng buộc vào sid + User-Agent và tự hết hạn. Đây KHÔNG phải
/// phiên tài khoản: cookie km_auth vẫn chỉ được cấp sau khi mật khẩu/QR đã xác thực thành công.
/// </summary>
public sealed class LoginBootstrapService(IDataProtectionProvider protectionProvider, TimeProvider? timeProvider = null)
{
    public const string CookieName = "km_login_bootstrap";
    public const string Protocol = "preauth-v1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private const int MaxTokenLength = 4_096;
    private const int MaxSessionIdLength = 64;
    private const int MaxUserAgentLength = 400;
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("KetoanMini.LoginBootstrap.v1");
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public LoginBootstrapToken Create(string sessionId, string? userAgent, TimeSpan? lifetime = null)
    {
        var sid = NormalizeSessionId(sessionId)
            ?? throw new ArgumentException("Login bootstrap requires a valid browser session id.", nameof(sessionId));
        var expiresAt = _clock.GetUtcNow().Add(lifetime ?? Lifetime);
        var ticket = new LoginBootstrapTicket(
            Version: 1,
            SessionId: sid,
            UserAgentHash: HashUserAgent(userAgent),
            Nonce: Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            ExpiresAtUnixSeconds: expiresAt.ToUnixTimeSeconds());
        return new LoginBootstrapToken(
            _protector.Protect(JsonSerializer.Serialize(ticket)),
            expiresAt.UtcDateTime);
    }

    public bool TryRead(string? protectedToken, string? sessionId, string? userAgent)
    {
        var sid = NormalizeSessionId(sessionId);
        if (sid is null || string.IsNullOrWhiteSpace(protectedToken) || protectedToken.Length > MaxTokenLength)
            return false;

        try
        {
            var ticket = JsonSerializer.Deserialize<LoginBootstrapTicket>(_protector.Unprotect(protectedToken));
            if (ticket is null || ticket.Version != 1 ||
                ticket.ExpiresAtUnixSeconds <= _clock.GetUtcNow().ToUnixTimeSeconds() ||
                string.IsNullOrWhiteSpace(ticket.Nonce) || ticket.Nonce.Length != 32 ||
                !string.Equals(ticket.SessionId, sid, StringComparison.Ordinal))
                return false;

            var expectedUaHash = Encoding.UTF8.GetBytes(HashUserAgent(userAgent));
            var actualUaHash = Encoding.UTF8.GetBytes(ticket.UserAgentHash ?? "");
            return expectedUaHash.Length == actualUaHash.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedUaHash, actualUaHash);
        }
        catch
        {
            // Sai chữ ký, sai định dạng hoặc vé từ key cũ đều fail-closed.
            return false;
        }
    }

    public LoginBootstrapToken IssueCookie(HttpContext http, string sessionId)
    {
        var token = Create(sessionId, http.Request.Headers.UserAgent.ToString());
        http.Response.Cookies.Append(CookieName, token.Value, CookieOptions(http, token.ExpiresAt));
        return token;
    }

    public bool ValidateCookie(HttpContext http, string? sessionId)
        => TryRead(
            http.Request.Cookies[CookieName],
            sessionId,
            http.Request.Headers.UserAgent.ToString());

    public static void ClearCookie(HttpContext http)
        => http.Response.Cookies.Delete(CookieName, CookieOptions(http, expiresAt: null));

    public static string? NormalizeSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        var value = sessionId.Trim();
        if (value.Length > MaxSessionIdLength || value.Any(char.IsControl)) return null;
        return value;
    }

    private static string HashUserAgent(string? userAgent)
    {
        var value = userAgent ?? "";
        if (value.Length > MaxUserAgentLength) value = value[..MaxUserAgentLength];
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static CookieOptions CookieOptions(HttpContext http, DateTime? expiresAt)
        => new()
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = expiresAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(expiresAt.Value, DateTimeKind.Utc)),
            IsEssential = true,
        };
}

public sealed record LoginBootstrapToken(string Value, DateTime ExpiresAt);

public sealed record LoginBootstrapTicket(
    int Version,
    string SessionId,
    string UserAgentHash,
    string Nonce,
    long ExpiresAtUnixSeconds);
