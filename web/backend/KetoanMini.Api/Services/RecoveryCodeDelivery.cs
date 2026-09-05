using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Services;

/// <summary>
/// Đường đưa MÃ KHÔI PHỤC MẬT KHẨU tới tay người dùng.
///
/// Hôm nay hệ thống chỉ có một kênh duy nhất là <b>cấp tay</b>: quản trị viên bấm nút, đọc mã cho
/// nhân viên. Lớp này tách phần "sinh mã" khỏi phần "chuyển mã" để về sau bật thêm thư điện tử hay
/// Zalo mà không phải sửa lại endpoint hay giao diện — chỉ cần điền cấu hình.
///
/// Cấu hình nằm ở khối <c>Recovery</c> (đặt trong appsettings.Local.json vì có mật khẩu và khoá):
///
///   "Recovery": {
///     "Email": { "Enabled": true, "Host": "smtp...", "Port": 587, "UseSsl": true,
///                "Username": "...", "Password": "...", "From": "no-reply@congty.vn",
///                "Subject": "Mã khôi phục mật khẩu", "Body": "Xin chào {name}, mã của bạn là {code}." },
///     "Zalo":  { "Enabled": true, "Endpoint": "https://business.openapi.zalo.me/message/template",
///                "AccessToken": "...", "TemplateId": "...", "BodyTemplate": "{...}" }
///   }
///
/// Thứ tự chọn kênh khi bên gọi không chỉ định: Zalo → Email → cấp tay. Kênh nào tắt hoặc thiếu địa
/// chỉ của người nhận thì tự bỏ qua, cuối cùng luôn còn đường cấp tay nên không bao giờ kẹt.
/// </summary>
public sealed class RecoveryCodeDelivery(
    IEnumerable<IRecoveryCodeSender> senders,
    ILogger<RecoveryCodeDelivery> log)
{
    private readonly List<IRecoveryCodeSender> _senders = senders.ToList();

    /// <summary>Các kênh TỰ ĐỘNG đang bật (không tính kênh cấp tay).</summary>
    public IReadOnlyList<string> AutoChannels =>
        _senders.Where(s => s.Enabled && s.Channel != RecoveryChannels.Manual).Select(s => s.Channel).ToList();

    /// <summary>Có kênh tự động nào dùng được cho người này không (đã bật và có địa chỉ để gửi).</summary>
    public bool CanAutoSend(RecoveryRecipient recipient) => PickAuto(recipient) is not null;

    private IRecoveryCodeSender? PickAuto(RecoveryRecipient recipient) =>
        _senders.FirstOrDefault(s =>
            s.Enabled && s.Channel != RecoveryChannels.Manual && !string.IsNullOrWhiteSpace(s.Target(recipient)));

    /// <summary>
    /// Chuyển mã cho người nhận. <paramref name="channel"/> để trống là tự chọn kênh tốt nhất.
    /// Không bao giờ ném lỗi ra ngoài: hỏng kênh thì rơi về cấp tay và nói rõ vì sao.
    /// </summary>
    public async Task<RecoveryDeliveryResult> SendAsync(
        RecoveryRecipient recipient, string code, string? channel = null, CancellationToken ct = default)
    {
        var wanted = (channel ?? "").Trim().ToLowerInvariant();
        var sender = wanted.Length > 0
            ? _senders.FirstOrDefault(s => s.Channel == wanted)
            : PickAuto(recipient);

        if (sender is null || sender.Channel == RecoveryChannels.Manual)
            return RecoveryDeliveryResult.Manual();

        if (!sender.Enabled)
            return RecoveryDeliveryResult.Manual($"Kênh {ChannelLabel(sender.Channel)} chưa được bật trong cấu hình.");

        var target = sender.Target(recipient);
        if (string.IsNullOrWhiteSpace(target))
            return RecoveryDeliveryResult.Manual($"Tài khoản này chưa có {AddressLabel(sender.Channel)} để gửi mã.");

        try
        {
            return await sender.SendAsync(recipient, code, ct);
        }
        catch (Exception ex)
        {
            // Gửi hỏng KHÔNG được làm mất mã: mã đã ghi vào CSDL rồi, chỉ cần quản trị viên đọc tay.
            log.LogWarning(ex, "Không gửi được mã khôi phục qua {Channel} cho {User}", sender.Channel, recipient.Username);
            return RecoveryDeliveryResult.Manual($"Gửi qua {ChannelLabel(sender.Channel)} không thành công, hãy đọc mã cho người dùng.");
        }
    }

    /// <summary>Đọc thông tin liên hệ của một tài khoản để biết gửi mã đi đâu.</summary>
    public static async Task<RecoveryRecipient?> LoadRecipientAsync(
        NpgsqlConnection conn, string username, CancellationToken ct = default)
    {
        await using var r = await conn.Cmd("""
            SELECT u.username,
                   COALESCE(NULLIF(e.full_name,''), NULLIF(u.full_name,''), u.username) AS full_name,
                   COALESCE(NULLIF(e.email,''), NULLIF(u.email,''), '') AS email,
                   COALESCE(e.phone, '') AS phone
            FROM app_users u
            LEFT JOIN hr_employees e
              ON e.user_id = u.id OR (e.user_id IS NULL AND lower(e.username) = lower(u.username))
            WHERE lower(u.username) = lower(@u) AND u.is_deleted = FALSE
            ORDER BY (e.user_id = u.id) DESC
            LIMIT 1
            """).With("@u", username).ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new RecoveryRecipient(r.Str("username"), r.Str("full_name"), r.Str("email"), r.Str("phone"))
            : null;
    }

    public static string ChannelLabel(string channel) => channel switch
    {
        RecoveryChannels.Email => "thư điện tử",
        RecoveryChannels.Zalo => "Zalo",
        _ => "cấp tay",
    };

    private static string AddressLabel(string channel) => channel switch
    {
        RecoveryChannels.Email => "địa chỉ thư điện tử",
        RecoveryChannels.Zalo => "số điện thoại",
        _ => "thông tin liên hệ",
    };

    /// <summary>Che bớt địa chỉ trước khi trả cho giao diện: đủ để nhận ra, không đủ để lộ.</summary>
    public static string Mask(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0) return "";
        var at = value.IndexOf('@');
        if (at > 0)
        {
            var name = value[..at];
            var head = name.Length <= 2 ? name[..1] : name[..2];
            return $"{head}{new string('*', Math.Max(1, name.Length - head.Length))}{value[at..]}";
        }
        return value.Length <= 4 ? new string('*', value.Length) : new string('*', value.Length - 3) + value[^3..];
    }
}

public static class RecoveryChannels
{
    public const string Manual = "manual";
    public const string Email = "email";
    public const string Zalo = "zalo";
}

/// <summary>Người nhận mã và các địa chỉ có thể gửi tới.</summary>
public sealed record RecoveryRecipient(string Username, string FullName, string Email, string Phone);

/// <param name="Delivered">Mã đã đi khỏi máy chủ hay chưa.</param>
/// <param name="RevealCode">
/// Có được đưa mã cho người bấm nút xem không. Cấp tay thì có — đó là cả cách hoạt động; gửi qua thư
/// hay Zalo thì KHÔNG, vì lúc đó chỉ chủ tài khoản mới cần biết mã.
/// </param>
public sealed record RecoveryDeliveryResult(bool Delivered, string Channel, string Message, bool RevealCode, string? SentTo = null)
{
    public static RecoveryDeliveryResult Manual(string? message = null) =>
        new(false, RecoveryChannels.Manual, message ?? "Đọc mã này cho người dùng. Hệ thống không tự gửi đi đâu cả.", true);

    public static RecoveryDeliveryResult Sent(string channel, string sentTo) =>
        new(true, channel, $"Đã gửi mã qua {RecoveryCodeDelivery.ChannelLabel(channel)} tới {sentTo}.", false, sentTo);
}

public interface IRecoveryCodeSender
{
    /// <summary>Tên kênh, khớp hằng số trong <see cref="RecoveryChannels"/>.</summary>
    string Channel { get; }

    /// <summary>Cấu hình đã đủ để gửi chưa.</summary>
    bool Enabled { get; }

    /// <summary>Địa chỉ sẽ gửi tới, rỗng nghĩa là người này không nhận được qua kênh đó.</summary>
    string Target(RecoveryRecipient recipient);

    Task<RecoveryDeliveryResult> SendAsync(RecoveryRecipient recipient, string code, CancellationToken ct);
}

/// <summary>Kênh mặc định: không gửi đi đâu, chỉ trả mã cho quản trị viên đọc.</summary>
public sealed class ManualRecoveryCodeSender : IRecoveryCodeSender
{
    public string Channel => RecoveryChannels.Manual;
    public bool Enabled => true;
    public string Target(RecoveryRecipient recipient) => recipient.Username;
    public Task<RecoveryDeliveryResult> SendAsync(RecoveryRecipient recipient, string code, CancellationToken ct)
        => Task.FromResult(RecoveryDeliveryResult.Manual());
}

/// <summary>
/// Gửi mã qua thư điện tử bằng SMTP. Tắt sẵn; điền khối <c>Recovery:Email</c> là chạy, không phải
/// sửa mã nguồn. Thân thư nhận ba chỗ thay: <c>{name}</c>, <c>{code}</c>, <c>{username}</c>.
/// </summary>
public sealed class EmailRecoveryCodeSender(IConfiguration config) : IRecoveryCodeSender
{
    private IConfigurationSection Section => config.GetSection("Recovery:Email");

    public string Channel => RecoveryChannels.Email;

    public bool Enabled =>
        Section.GetValue("Enabled", false)
        && !string.IsNullOrWhiteSpace(Section["Host"])
        && !string.IsNullOrWhiteSpace(Section["From"]);

    public string Target(RecoveryRecipient recipient) => recipient.Email;

    public async Task<RecoveryDeliveryResult> SendAsync(RecoveryRecipient recipient, string code, CancellationToken ct)
    {
        var subject = Fill(Section["Subject"] ?? "Mã khôi phục mật khẩu", recipient, code);
        var body = Fill(
            Section["Body"] ?? "Xin chào {name},\n\nMã khôi phục mật khẩu của bạn là: {code}\n\nMã có hiệu lực 7 ngày và chỉ dùng được một lần.",
            recipient, code);

        using var client = new SmtpClient(Section["Host"], Section.GetValue("Port", 587))
        {
            EnableSsl = Section.GetValue("UseSsl", true),
        };
        var user = Section["Username"];
        if (!string.IsNullOrWhiteSpace(user))
            client.Credentials = new NetworkCredential(user, Section["Password"] ?? "");

        using var message = new MailMessage(Section["From"]!, recipient.Email, subject, body)
        {
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };
        await client.SendMailAsync(message, ct);
        return RecoveryDeliveryResult.Sent(Channel, RecoveryCodeDelivery.Mask(recipient.Email));
    }

    private static string Fill(string template, RecoveryRecipient r, string code) =>
        template.Replace("{name}", r.FullName).Replace("{code}", code).Replace("{username}", r.Username);
}

/// <summary>
/// Gửi mã qua Zalo. Mặc định dựng theo Zalo Notification Service (ZNS) — gửi tới SỐ ĐIỆN THOẠI bằng
/// một mẫu tin đã được Zalo duyệt. Tắt sẵn.
///
/// Toàn bộ hình dạng request nằm trong cấu hình (<c>Endpoint</c>, <c>AccessToken</c>,
/// <c>TemplateId</c>, <c>BodyTemplate</c>) nên khi Zalo đổi API, hoặc khi công ty dùng một cổng gửi
/// tin khác, chỉ phải sửa appsettings chứ không phải sửa mã nguồn. Thân request nhận các chỗ thay
/// <c>{phone}</c>, <c>{code}</c>, <c>{name}</c>, <c>{username}</c>, <c>{templateId}</c>.
/// </summary>
public sealed class ZaloRecoveryCodeSender(IConfiguration config, IHttpClientFactory httpClientFactory) : IRecoveryCodeSender
{
    private const string DefaultEndpoint = "https://business.openapi.zalo.me/message/template";
    private const string DefaultBody =
        """{"phone":"{phone}","template_id":"{templateId}","template_data":{"code":"{code}","name":"{name}"}}""";

    private IConfigurationSection Section => config.GetSection("Recovery:Zalo");

    public string Channel => RecoveryChannels.Zalo;

    public bool Enabled =>
        Section.GetValue("Enabled", false)
        && !string.IsNullOrWhiteSpace(Section["AccessToken"])
        && !string.IsNullOrWhiteSpace(Section["TemplateId"]);

    /// <summary>ZNS gửi theo số điện thoại; số phải ở dạng 84xxxxxxxxx.</summary>
    public string Target(RecoveryRecipient recipient) => NormalizePhone(recipient.Phone);

    public async Task<RecoveryDeliveryResult> SendAsync(RecoveryRecipient recipient, string code, CancellationToken ct)
    {
        var phone = NormalizePhone(recipient.Phone);
        var body = (Section["BodyTemplate"] ?? DefaultBody)
            .Replace("{phone}", JsonEncode(phone))
            .Replace("{code}", JsonEncode(code))
            .Replace("{name}", JsonEncode(recipient.FullName))
            .Replace("{username}", JsonEncode(recipient.Username))
            .Replace("{templateId}", JsonEncode(Section["TemplateId"] ?? ""));

        var http = httpClientFactory.CreateClient(nameof(ZaloRecoveryCodeSender));
        using var request = new HttpRequestMessage(HttpMethod.Post, Section["Endpoint"] ?? DefaultEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("access_token", Section["AccessToken"]);

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Zalo trả {(int)response.StatusCode}: {Trim(text)}");

        // Zalo trả HTTP 200 kể cả khi thất bại; mã lỗi thật nằm ở trường "error" trong thân phản hồi.
        if (ReadErrorCode(text) is { } error && error != 0)
            throw new InvalidOperationException($"Zalo báo lỗi {error}: {Trim(text)}");

        return RecoveryDeliveryResult.Sent(Channel, RecoveryCodeDelivery.Mask(phone));
    }

    private static int? ReadErrorCode(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("error", out var error) && error.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"0912 345 678" → "84912345678". Trả rỗng nếu không ra số dùng được.</summary>
    private static string NormalizePhone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 9) return "";
        if (digits.StartsWith("84", StringComparison.Ordinal)) return digits;
        if (digits.StartsWith('0')) return "84" + digits[1..];
        return "84" + digits;
    }

    private static string JsonEncode(string value) => JsonEncodedText.Encode(value).ToString();

    private static string Trim(string value) => value.Length > 300 ? value[..300] : value;
}
