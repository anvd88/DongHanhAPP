using KetoanMini.Api.Security;

namespace KetoanMini.Communication.Security;

/// <summary>
/// Origin policy extracted with the cookie-authenticated WebSocket host. Browsers attach cookies
/// automatically to a WebSocket handshake, so a communication host must reject foreign origins.
/// </summary>
public static class CommunicationOriginPolicy
{
    public static bool IsAllowedOrigin(HttpContext ctx, string[] configuredOrigins)
    {
        var origin = ctx.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return true;

        var self = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        if (string.Equals(origin, self, StringComparison.OrdinalIgnoreCase)) return true;

        // Behind a trusted reverse proxy, Kestrel may see HTTP while the browser uses HTTPS.
        if (origin.EndsWith($"://{ctx.Request.Host}", StringComparison.OrdinalIgnoreCase)) return true;

        return CorsPolicy.IsAllowed(origin, configuredOrigins);
    }
}
