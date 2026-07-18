using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace KetoanMini.Api.Services;

/// <summary>
/// Kho phiên đăng nhập QR ngắn hạn. Phiên chỉ sống 5 phút trong RAM, nên các lần poll không chạm DB.
/// Hai token độc lập được dùng cho hai phía: token trong QR để app xác nhận và poll token chỉ trình
/// duyệt biết để nhận JWT. Server chỉ giữ SHA-256 của token, không giữ bản rõ.
/// </summary>
public sealed class QrLoginService(TimeProvider? timeProvider = null) : BackgroundService
{
    public const string QrPrefix = "ketoanmini-login:";
    public const string MobileAppPrefix = "ketoanmini-app-login:";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(5);

    private const int TokenBytes = 32;
    private const int MaxActiveSessions = 5_000;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Session> _byPollHash = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _pollHashByScanHash = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _pollHashByBrowserSid = new(StringComparer.Ordinal);
    private readonly object _browserSessionGate = new();

    public QrLoginCreated? Create(
        string browserSid,
        string userAgent,
        WebLoginChannel channel = WebLoginChannel.DesktopQr)
    {
        // Bộ dọn nền chạy mỗi phút. Chỉ quét O(n) ngay tại request khi gần chạm trần để đường tạo QR
        // thông thường luôn O(1), kể cả khi server đang có nhiều phiên chờ.
        if (_byPollHash.Count >= MaxActiveSessions)
        {
            PurgeExpired();
            if (_byPollHash.Count >= MaxActiveSessions) return null;
        }

        // Xác suất đụng token 256-bit là không đáng kể; vòng lặp vẫn bảo đảm không ghi đè nếu xảy ra.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var scanToken = NewToken();
            var pollToken = NewToken();
            var scanHash = Hash(scanToken);
            var pollHash = Hash(pollToken);
            var expiresAt = _clock.GetUtcNow().Add(SessionLifetime);
            var session = new Session(pollHash, scanHash, browserSid, userAgent, expiresAt, channel);

            if (!_byPollHash.TryAdd(pollHash, session)) continue;
            if (_pollHashByScanHash.TryAdd(scanHash, pollHash))
            {
                // A browser can refresh or open the QR modal again before its best-effort cancel arrives.
                // Installing the new session atomically makes the latest QR authoritative server-side.
                lock (_browserSessionGate)
                {
                    if (_pollHashByBrowserSid.TryGetValue(browserSid, out var previousPollHash) &&
                        !string.Equals(previousPollHash, pollHash, StringComparison.Ordinal) &&
                        _byPollHash.TryGetValue(previousPollHash, out var previousSession))
                    {
                        lock (previousSession.Gate)
                        {
                            previousSession.State = SessionState.Canceled;
                        }

                        Remove(previousSession);
                    }

                    _pollHashByBrowserSid[browserSid] = pollHash;
                }

                var prefix = channel == WebLoginChannel.MobileApp ? MobileAppPrefix : QrPrefix;
                return new QrLoginCreated(prefix + scanToken, pollToken, expiresAt.UtcDateTime);
            }

            _byPollHash.TryRemove(pollHash, out _);
        }

        return null;
    }

    /// <summary>Chỉ server phân loại QR đăng nhập; APK gửi nguyên nội dung tới bộ phân giải chung.</summary>
    public static bool LooksLikeLoginQr(string? value)
        => value?.Trim().StartsWith(QrPrefix, StringComparison.Ordinal) == true;

    public static bool LooksLikeMobileAppLogin(string? value)
        => value?.Trim().StartsWith(MobileAppPrefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// Đánh dấu app đã quét mã nhưng CHƯA đồng ý đăng nhập. Trình duyệt chỉ nhận tên tài khoản qua
    /// poll token bí mật để hiển thị màn "chờ xác nhận" kiểu Zalo; JWT chưa được cấp ở bước này.
    /// </summary>
    public QrLoginScanResult Scan(
        string? qrCode,
        string username,
        string fullName,
        WebLoginChannel channel = WebLoginChannel.DesktopQr)
        => ScanDetailed(qrCode, username, fullName, channel).Result;

    /// <summary>
    /// Bản chi tiết dành cho giao thức QR tổng quát: trả cùng kết quả cũ và hạn thật của phiên web
    /// trong một lần khóa, để vé quyết định không vô tình sống lâu hơn mã QR ban đầu.
    /// </summary>
    public QrLoginScanOutcome ScanDetailed(
        string? qrCode,
        string username,
        string fullName,
        WebLoginChannel channel = WebLoginChannel.DesktopQr)
    {
        var scanToken = ParseScanToken(qrCode, channel);
        if (scanToken is null || !_pollHashByScanHash.TryGetValue(Hash(scanToken), out var pollHash) ||
            !_byPollHash.TryGetValue(pollHash, out var session) || session.Channel != channel)
            return new QrLoginScanOutcome(QrLoginScanResult.InvalidOrExpired, null);

        var remove = false;
        QrLoginScanResult result;
        DateTime? expiresAt = null;
        lock (session.Gate)
        {
            if (IsExpired(session))
            {
                remove = true;
                result = QrLoginScanResult.InvalidOrExpired;
            }
            else if (session.State == SessionState.Pending)
            {
                session.Username = username;
                session.FullName = fullName;
                session.State = SessionState.Scanned;
                result = QrLoginScanResult.Scanned;
            }
            else if (session.State == SessionState.Scanned &&
                     string.Equals(session.Username, username, StringComparison.Ordinal))
            {
                session.FullName = fullName;
                result = QrLoginScanResult.AlreadyScanned;
            }
            else
            {
                result = QrLoginScanResult.InvalidOrExpired;
            }

            if (result is QrLoginScanResult.Scanned or QrLoginScanResult.AlreadyScanned)
                expiresAt = session.ExpiresAt.UtcDateTime;
        }

        if (remove) Remove(session);
        return new QrLoginScanOutcome(result, expiresAt);
    }

    public QrLoginConfirmResult Confirm(
        string? qrCode,
        string username,
        string fullName,
        WebLoginChannel channel = WebLoginChannel.DesktopQr)
    {
        var scanToken = ParseScanToken(qrCode, channel);
        if (scanToken is null || !_pollHashByScanHash.TryGetValue(Hash(scanToken), out var pollHash) ||
            !_byPollHash.TryGetValue(pollHash, out var session) || session.Channel != channel)
            return QrLoginConfirmResult.InvalidOrExpired;

        var remove = false;
        QrLoginConfirmResult result;
        lock (session.Gate)
        {
            if (IsExpired(session))
            {
                remove = true;
                result = QrLoginConfirmResult.InvalidOrExpired;
            }
            else if (session.State == SessionState.Pending)
            {
                // Tương thích APK cũ: bản cũ chưa có bước /scan nên vẫn được xác nhận trực tiếp.
                session.Username = username;
                session.FullName = fullName;
                session.State = SessionState.Confirmed;
                result = QrLoginConfirmResult.Confirmed;
            }
            else if (session.State == SessionState.Scanned &&
                     string.Equals(session.Username, username, StringComparison.Ordinal))
            {
                session.FullName = fullName;
                session.State = SessionState.Confirmed;
                result = QrLoginConfirmResult.Confirmed;
            }
            else if (session.State == SessionState.Confirmed &&
                     string.Equals(session.Username, username, StringComparison.Ordinal))
            {
                // Idempotent: app có thể gửi lại khi phản hồi mạng đầu tiên bị thất lạc.
                result = QrLoginConfirmResult.AlreadyConfirmed;
            }
            else
            {
                result = QrLoginConfirmResult.InvalidOrExpired;
            }
        }

        if (remove) Remove(session);
        return result;
    }

    /// <summary>
    /// Điện thoại từ chối ở màn xác nhận. Phiên trở thành trạng thái kết thúc để trình duyệt báo rõ
    /// kết quả; người khác không thể quét lại cùng ảnh QR sau khi chủ tài khoản đã từ chối.
    /// </summary>
    public bool RejectScan(
        string? qrCode,
        string username,
        WebLoginChannel channel = WebLoginChannel.DesktopQr)
    {
        var scanToken = ParseScanToken(qrCode, channel);
        if (scanToken is null || !_pollHashByScanHash.TryGetValue(Hash(scanToken), out var pollHash) ||
            !_byPollHash.TryGetValue(pollHash, out var session) || session.Channel != channel)
            return false;

        var remove = false;
        var rejected = false;
        lock (session.Gate)
        {
            if (IsExpired(session))
            {
                remove = true;
            }
            else if (session.State == SessionState.Scanned &&
                     string.Equals(session.Username, username, StringComparison.Ordinal))
            {
                session.State = SessionState.Rejected;
                rejected = true;
            }
            else if (session.State == SessionState.Rejected &&
                     string.Equals(session.Username, username, StringComparison.Ordinal))
            {
                // Idempotent khi phản hồi reject đầu tiên bị thất lạc.
                rejected = true;
            }
        }

        if (remove) Remove(session);
        return rejected;
    }

    /// <summary>
    /// Kiểm tra phiên và giành quyền cấp token nếu app đã xác nhận. Chỉ một poll đồng thời được chuyển
    /// Confirmed -> Consuming; nếu xử lý DB lỗi, CompleteConsume(false) trả phiên về Confirmed để thử lại.
    /// </summary>
    public QrLoginPollResult BeginConsume(
        string? pollToken,
        WebLoginChannel channel = WebLoginChannel.DesktopQr)
    {
        if (!IsTokenShapeValid(pollToken) || !_byPollHash.TryGetValue(Hash(pollToken!), out var session) ||
            session.Channel != channel)
            return QrLoginPollResult.Expired();

        var remove = false;
        QrLoginPollResult result;
        lock (session.Gate)
        {
            if (IsExpired(session))
            {
                remove = true;
                result = QrLoginPollResult.Expired(session.ExpiresAt.UtcDateTime);
            }
            else if ((session.State is SessionState.Confirmed or SessionState.Authorized) &&
                     !string.IsNullOrWhiteSpace(session.Username))
            {
                var alreadyAuthorized = session.State == SessionState.Authorized;
                session.State = SessionState.Consuming;
                result = QrLoginPollResult.Ready(new QrLoginSession(
                    session.PollHash,
                    session.Username,
                    session.BrowserSid,
                    session.UserAgent,
                    session.ExpiresAt.UtcDateTime,
                    alreadyAuthorized,
                    session.Channel));
            }
            else if (session.State == SessionState.Scanned && !string.IsNullOrWhiteSpace(session.Username))
            {
                result = QrLoginPollResult.Scanned(
                    session.ExpiresAt.UtcDateTime,
                    new QrLoginAccount(session.Username, session.FullName));
            }
            else if (session.State == SessionState.Rejected)
            {
                result = QrLoginPollResult.Rejected(session.ExpiresAt.UtcDateTime);
            }
            else if (session.State is SessionState.Pending or SessionState.Consuming)
            {
                result = QrLoginPollResult.Pending(session.ExpiresAt.UtcDateTime);
            }
            else
            {
                remove = true;
                result = QrLoginPollResult.Expired(session.ExpiresAt.UtcDateTime);
            }
        }

        if (remove) Remove(session);
        return result;
    }

    /// <summary>
    /// Đọc tài khoản đã quét bằng poll token bí mật. Dùng cho một request tải ảnh đại diện riêng,
    /// tránh giữ data URL ảnh (có thể lớn) trong kho phiên RAM hoặc trả lại ở mọi nhịp poll.
    /// </summary>
    public QrLoginAccount? GetScannedAccount(
        string? pollToken,
        WebLoginChannel channel = WebLoginChannel.DesktopQr)
    {
        if (!IsTokenShapeValid(pollToken) || !_byPollHash.TryGetValue(Hash(pollToken!), out var session) ||
            session.Channel != channel)
            return null;

        var remove = false;
        QrLoginAccount? account = null;
        lock (session.Gate)
        {
            if (IsExpired(session))
            {
                remove = true;
            }
            else if ((session.State is SessionState.Scanned or SessionState.Confirmed or SessionState.Consuming or SessionState.Authorized) &&
                     !string.IsNullOrWhiteSpace(session.Username))
            {
                account = new QrLoginAccount(session.Username, session.FullName);
            }
        }

        if (remove) Remove(session);
        return account;
    }

    public void CompleteConsume(QrLoginSession snapshot, bool success)
    {
        if (!_byPollHash.TryGetValue(snapshot.PollHash, out var session) || session.Channel != snapshot.Channel) return;

        var remove = false;
        lock (session.Gate)
        {
            if (session.State != SessionState.Consuming) return;
            if (IsExpired(session))
            {
                session.State = SessionState.Consumed;
                remove = true;
            }
            else if (success)
            {
                // Giữ kết quả ủy quyền rất ngắn để poll có thể thử lại nếu response chứa JWT bị mất.
                // Trình duyệt gọi Acknowledge ngay sau khi đã lưu token để xóa phiên khỏi RAM.
                session.State = SessionState.Authorized;
            }
            else
            {
                session.State = snapshot.AlreadyAuthorized ? SessionState.Authorized : SessionState.Confirmed;
            }
        }

        if (remove) Remove(session);
    }

    public void Invalidate(QrLoginSession snapshot)
    {
        if (!_byPollHash.TryGetValue(snapshot.PollHash, out var session)) return;
        lock (session.Gate)
        {
            if (session.State != SessionState.Consuming) return;
            session.State = SessionState.Consumed;
        }
        Remove(session);
    }

    /// <summary>Xóa phiên sau khi trình duyệt xác nhận đã nhận và lưu JWT.</summary>
    public void Acknowledge(
        string? pollToken,
        WebLoginChannel channel = WebLoginChannel.DesktopQr)
    {
        if (!IsTokenShapeValid(pollToken) || !_byPollHash.TryGetValue(Hash(pollToken!), out var session) ||
            session.Channel != channel) return;
        var remove = false;
        lock (session.Gate)
        {
            if (session.State == SessionState.Authorized)
            {
                session.State = SessionState.Consumed;
                remove = true;
            }
        }
        if (remove) Remove(session);
    }

    public void Cancel(
        string? pollToken,
        WebLoginChannel channel = WebLoginChannel.DesktopQr)
    {
        if (!IsTokenShapeValid(pollToken) || !_byPollHash.TryGetValue(Hash(pollToken!), out var session) ||
            session.Channel != channel) return;
        lock (session.Gate)
        {
            if (session.State == SessionState.Consuming) return;
            session.State = SessionState.Canceled;
        }
        Remove(session);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) PurgeExpired();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Dừng ứng dụng bình thường.
        }
    }

    private void PurgeExpired()
    {
        foreach (var session in _byPollHash.Values)
        {
            var remove = false;
            lock (session.Gate)
            {
                remove = IsExpired(session) || session.State is SessionState.Consumed or SessionState.Canceled;
            }
            if (remove) Remove(session);
        }
    }

    private bool IsExpired(Session session) => _clock.GetUtcNow() >= session.ExpiresAt;

    private void Remove(Session session)
    {
        _byPollHash.TryRemove(new KeyValuePair<string, Session>(session.PollHash, session));
        _pollHashByScanHash.TryRemove(new KeyValuePair<string, string>(session.ScanHash, session.PollHash));
        _pollHashByBrowserSid.TryRemove(new KeyValuePair<string, string>(session.BrowserSid, session.PollHash));
    }

    private static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string? ParseScanToken(string? value, WebLoginChannel channel)
    {
        var text = value?.Trim();
        var prefix = channel == WebLoginChannel.MobileApp ? MobileAppPrefix : QrPrefix;
        if (string.IsNullOrEmpty(text) || !text.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var token = text[prefix.Length..];
        return IsTokenShapeValid(token) ? token : null;
    }

    private static bool IsTokenShapeValid(string? token)
    {
        if (token is null || token.Length is < 40 or > 64) return false;
        foreach (var c in token)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return false;
        return true;
    }

    private sealed class Session(
        string pollHash,
        string scanHash,
        string browserSid,
        string userAgent,
        DateTimeOffset expiresAt,
        WebLoginChannel channel)
    {
        public object Gate { get; } = new();
        public string PollHash { get; } = pollHash;
        public string ScanHash { get; } = scanHash;
        public string BrowserSid { get; } = browserSid;
        public string UserAgent { get; } = userAgent;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public WebLoginChannel Channel { get; } = channel;
        public SessionState State { get; set; } = SessionState.Pending;
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
    }

    private enum SessionState { Pending, Scanned, Confirmed, Consuming, Authorized, Consumed, Rejected, Canceled }
}

public enum QrLoginScanResult { Scanned, AlreadyScanned, InvalidOrExpired }

public sealed record QrLoginScanOutcome(QrLoginScanResult Result, DateTime? ExpiresAt);

public enum QrLoginConfirmResult { Confirmed, AlreadyConfirmed, InvalidOrExpired }

public enum QrLoginPollState { Pending, Scanned, Rejected, Ready, Expired }

public enum WebLoginChannel { DesktopQr, MobileApp }

public sealed record QrLoginCreated(string QrCode, string PollToken, DateTime ExpiresAt);

public sealed record QrLoginSession(
    string PollHash,
    string Username,
    string BrowserSid,
    string UserAgent,
    DateTime ExpiresAt,
    bool AlreadyAuthorized,
    WebLoginChannel Channel = WebLoginChannel.DesktopQr);

public sealed record QrLoginAccount(string Username, string FullName);

public sealed record QrLoginPollResult(
    QrLoginPollState State,
    DateTime ExpiresAt,
    QrLoginSession? Session,
    QrLoginAccount? Account)
{
    public static QrLoginPollResult Pending(DateTime expiresAt) => new(QrLoginPollState.Pending, expiresAt, null, null);
    public static QrLoginPollResult Scanned(DateTime expiresAt, QrLoginAccount account) =>
        new(QrLoginPollState.Scanned, expiresAt, null, account);
    public static QrLoginPollResult Rejected(DateTime expiresAt) =>
        new(QrLoginPollState.Rejected, expiresAt, null, null);
    public static QrLoginPollResult Ready(QrLoginSession session) => new(QrLoginPollState.Ready, session.ExpiresAt, session, null);
    public static QrLoginPollResult Expired(DateTime expiresAt = default) => new(QrLoginPollState.Expired, expiresAt, null, null);
}
