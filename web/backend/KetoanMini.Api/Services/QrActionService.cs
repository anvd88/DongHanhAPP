using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace KetoanMini.Api.Services;

/// <summary>
/// Vé quyết định QR tự chứa, được mã hóa/xác thực bằng ASP.NET Data Protection. Vé gắn với đúng tài
/// khoản + phiên app và tự hết hạn nên không cần dictionary/cache mới trong RAM.
/// </summary>
public sealed class QrActionTokenService(IDataProtectionProvider protectionProvider, TimeProvider? timeProvider = null)
{
    private const int MaxTokenLength = 12_000;
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("KetoanMini.QrActions.v1");
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public QrDecisionToken Create(
        string handler,
        string value,
        string subjectId,
        string sessionId,
        IReadOnlyCollection<string> allowedActionIds,
        TimeSpan lifetime)
        => CreateUntil(handler, value, subjectId, sessionId, allowedActionIds,
            _clock.GetUtcNow().Add(lifetime).UtcDateTime);

    public QrDecisionToken CreateUntil(
        string handler,
        string value,
        string subjectId,
        string sessionId,
        IReadOnlyCollection<string> allowedActionIds,
        DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("QR decision token requires a user subject and session id.");
        var expiresAt = new DateTimeOffset(expiresAtUtc.ToUniversalTime());
        var ticket = new QrDecisionTicket(
            Version: 1,
            Handler: handler,
            Value: value,
            SubjectId: subjectId,
            SessionId: sessionId,
            AllowedActionIds: allowedActionIds.Distinct(StringComparer.Ordinal).ToArray(),
            ExpiresAtUnixSeconds: expiresAt.ToUnixTimeSeconds());
        return new QrDecisionToken(_protector.Protect(JsonSerializer.Serialize(ticket)), expiresAt.UtcDateTime);
    }

    public bool TryRead(
        string? protectedToken,
        string subjectId,
        string sessionId,
        string actionId,
        out QrDecisionTicket ticket)
    {
        ticket = QrDecisionTicket.Empty;
        if (string.IsNullOrWhiteSpace(protectedToken) || protectedToken.Length > MaxTokenLength ||
            string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(sessionId) ||
            string.IsNullOrWhiteSpace(actionId))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<QrDecisionTicket>(_protector.Unprotect(protectedToken));
            if (parsed is null || parsed.Version != 1 || string.IsNullOrWhiteSpace(parsed.Handler) ||
                parsed.ExpiresAtUnixSeconds <= _clock.GetUtcNow().ToUnixTimeSeconds() ||
                parsed.Value.Length is 0 or > QrActionProtocol.MaxQrValueLength ||
                !string.Equals(parsed.SubjectId, subjectId, StringComparison.Ordinal) ||
                !string.Equals(parsed.SessionId, sessionId, StringComparison.Ordinal) ||
                parsed.AllowedActionIds is null || !parsed.AllowedActionIds.Contains(actionId, StringComparer.Ordinal))
                return false;

            ticket = parsed;
            return true;
        }
        catch
        {
            // Token sai chữ ký, sai định dạng hoặc thuộc key cũ đều fail-closed.
            return false;
        }
    }
}

public sealed record QrDecisionToken(string Value, DateTime ExpiresAt);

public sealed record QrDecisionTicket(
    int Version,
    string Handler,
    string Value,
    string SubjectId,
    string SessionId,
    string[] AllowedActionIds,
    long ExpiresAtUnixSeconds)
{
    public static readonly QrDecisionTicket Empty = new(0, "", "", "", "", [], 0);
}

/// <summary>
/// Quy tắc QR đơn giản có thể đổi trực tiếp trong appsettings.Local.json. Hai loại cấu hình không cần
/// build lại backend/APK: message (hiện nội dung) và open_https_url (hỏi rồi mở trang HTTPS cố định).
/// Nghiệp vụ server phức tạp hơn có thể thêm handler mới nhưng vẫn dùng nguyên protocol APK này.
/// </summary>
public sealed class QrScannerOptions
{
    public List<string> AllowedHttpsHosts { get; set; } = [];
    public List<QrConfiguredAction> Actions { get; set; } = [];
}

public sealed class QrConfiguredAction
{
    public bool Enabled { get; set; } = true;
    public string Id { get; set; } = "";
    public string ExactValue { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Kind { get; set; } = "message";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Url { get; set; } = "";
    public string OpenLabel { get; set; } = "Mở";
    public string CancelLabel { get; set; } = "Đóng";
}

public sealed class QrConfiguredActionRegistry(IOptionsMonitor<QrScannerOptions> options)
{
    public QrConfiguredAction? Match(string value)
    {
        var actions = (options.CurrentValue.Actions ?? [])
            .Where(IsUsable)
            .ToArray();

        return actions.FirstOrDefault(a =>
                   !string.IsNullOrEmpty(a.ExactValue) && string.Equals(a.ExactValue, value, StringComparison.Ordinal))
               ?? actions
                   .Where(a => !string.IsNullOrEmpty(a.Prefix) && value.StartsWith(a.Prefix, StringComparison.Ordinal))
                   .OrderByDescending(a => a.Prefix.Length)
                   .FirstOrDefault();
    }

    public bool TryGetAllowedHttpsUrl(QrConfiguredAction action, out string safeUrl)
    {
        safeUrl = "";
        var raw = action.Url?.Trim() ?? "";
        if (raw.Length is 0 or > 2_048 || raw.Any(char.IsControl) ||
            !Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.HostNameType != UriHostNameType.Dns ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (!uri.IsDefaultPort && uri.Port != 443))
            return false;

        string host;
        try
        {
            host = new IdnMapping().GetAscii(uri.Host.TrimEnd('.')).ToLowerInvariant();
        }
        catch
        {
            return false;
        }

        if (host.Length == 0 || host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
            return false;
        if (!(options.CurrentValue.AllowedHttpsHosts ?? []).Any(pattern => HostMatches(host, pattern))) return false;

        safeUrl = uri.AbsoluteUri;
        return true;
    }

    public static string PlainText(string? value, string fallback, int maxLength)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        text = new string(text.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray());
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static bool IsUsable(QrConfiguredAction? action)
    {
        if (action is null || !action.Enabled || string.IsNullOrWhiteSpace(action.Id) || action.Id.Length > 80 ||
            (string.IsNullOrEmpty(action.ExactValue) && string.IsNullOrEmpty(action.Prefix)))
            return false;
        return action.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    private static bool HostMatches(string host, string? configuredPattern)
    {
        var pattern = configuredPattern?.Trim().TrimEnd('.').ToLowerInvariant() ?? "";
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[2..];
            return suffix.Length > 0 && host.Length > suffix.Length &&
                   host.EndsWith("." + suffix, StringComparison.Ordinal);
        }
        return pattern.Length > 0 && string.Equals(host, pattern, StringComparison.Ordinal);
    }
}

public static class QrActionProtocol
{
    public const int Version = 1;
    public const int MaxQrValueLength = 4_096;
    public const string ServerDecision = "server_decision";
    public const string OpenHttpsUrl = "open_https_url";
    public const string Dismiss = "dismiss";
    public const string WebLoginHandler = "web_login";
    public const string ConfirmAction = "confirm";
    public const string RejectAction = "reject";
}
