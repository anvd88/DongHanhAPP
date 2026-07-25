using System.Security.Cryptography;

namespace KetoanMini.Api.Security;

/// <summary>
/// PHIÊN ĐĂNG NHẬP CHO TRÌNH DUYỆT bằng cookie HttpOnly.
///
/// Vì sao đổi: JWT để trong localStorage thì BẤT KỲ đoạn JavaScript nào chạy trên trang cũng đọc
/// được — một lỗ hổng XSS (kể cả trong một thư viện phụ thuộc) là đủ để lấy trọn token còn hạn và
/// dùng lại ở máy khác. Cookie HttpOnly thì JavaScript KHÔNG đọc được: XSS vẫn có thể gọi API thay
/// người dùng trong lúc trang còn mở, nhưng không mang được phiên đăng nhập đi nơi khác.
///
/// Đổi lại, cookie được trình duyệt gửi TỰ ĐỘNG ⇒ sinh ra nguy cơ CSRF (trang lạ ép trình duyệt gọi
/// API của ta). Vì thế đi kèm hai lớp:
///   1. SameSite=Lax — trình duyệt không gửi cookie cho request chéo site do fetch/XHR/form POST.
///   2. Cookie CSRF đọc được + header X-CSRF-Token (double-submit) — trang lạ không đọc được cookie
///      của ta nên không dựng được header khớp. Xem <see cref="ValidateCsrf"/>.
///
/// ỨNG DỤNG ANDROID KHÔNG DÙNG ĐƯỜNG NÀY: app native vẫn gửi Bearer token và lưu trong vùng bảo mật
/// của thiết bị — nó không có "ambient credential" nên cũng không có nguy cơ CSRF.
/// </summary>
public static class AuthCookies
{
    /// <summary>Cookie chứa JWT. HttpOnly ⇒ JavaScript không đọc được.</summary>
    public const string AuthCookie = "km_auth";

    /// <summary>Cookie chống CSRF. CỐ Ý không HttpOnly: frontend phải đọc được để gắn vào header.
    /// Nó không phải bí mật đăng nhập — biết giá trị này mà không có cookie phiên thì vô dụng.</summary>
    public const string CsrfCookie = "km_csrf";

    /// <summary>Header mang lại giá trị cookie CSRF (double-submit).</summary>
    public const string CsrfHeader = "X-CSRF-Token";

    /// <summary>Bật/tắt phiên bằng cookie cho web. Tắt = quay lại Bearer trong localStorage (không khuyến nghị).</summary>
    public static bool Enabled(IConfiguration config) => config.GetValue("Security:CookieAuth", true);

    /// <summary>Số giờ sống của phiên web. Ngắn hơn nhiều token của app (365 ngày) vì trình duyệt
    /// thường là máy dùng chung; phiên được GIA HẠN TRƯỢT khi còn hoạt động nên người dùng năng động
    /// không bị đá ra. Mặc định 168h = 7 ngày, khớp với ngưỡng phiên nhàn rỗi Security:SessionIdleDays.</summary>
    public static int WebExpireHours(IConfiguration config)
        => Math.Max(1, config.GetValue("Jwt:WebExpireHours", 168));

    /// <summary>Sinh giá trị CSRF ngẫu nhiên (128 bit, an toàn mật mã).</summary>
    public static string NewCsrfToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static CookieOptions BaseOptions(HttpContext ctx, bool httpOnly, DateTimeOffset? expires)
        => new()
        {
            HttpOnly = httpOnly,
            // Secure theo đúng cách request thật đi tới: production luôn HTTPS (đã ép ở Program.cs, và
            // UseForwardedHeaders khiến Request.IsHttps = true sau Cloudflare Tunnel) nên cookie có
            // Secure; còn dev chạy http://localhost thì không đặt Secure, nếu không trình duyệt vứt
            // cookie đi và không ai đăng nhập được ở máy lập trình.
            Secure = ctx.Request.IsHttps,
            // Lax chứ không Strict: Strict thì người dùng bấm link từ email/chat vào hệ thống sẽ hiện
            // ra như CHƯA đăng nhập (cookie không được gửi ở điều hướng đầu tiên), phải tải lại trang
            // mới thấy mình đã đăng nhập — khó hiểu với người dùng mà đổi lại chẳng thêm bao nhiêu an
            // toàn, vì fetch/XHR chéo site đã bị Lax chặn sẵn.
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires,
            IsEssential = true,
        };

    /// <summary>Đặt cookie phiên + cookie CSRF cho một lần đăng nhập trên trình duyệt.</summary>
    public static void Issue(HttpContext ctx, string jwt, DateTimeOffset expiresAt)
    {
        ctx.Response.Cookies.Append(AuthCookie, jwt, BaseOptions(ctx, httpOnly: true, expiresAt));
        ctx.Response.Cookies.Append(CsrfCookie, NewCsrfToken(), BaseOptions(ctx, httpOnly: false, expiresAt));
    }

    /// <summary>Gia hạn TRƯỢT: chỉ thay cookie phiên, GIỮ NGUYÊN giá trị CSRF hiện có. Đổi luôn giá trị
    /// CSRF ở đây sẽ đá hỏng các request đang bay cùng lúc (trang đã đọc giá trị cũ để gắn header).</summary>
    public static void Refresh(HttpContext ctx, string jwt, DateTimeOffset expiresAt)
    {
        ctx.Response.Cookies.Append(AuthCookie, jwt, BaseOptions(ctx, httpOnly: true, expiresAt));
        var csrf = ctx.Request.Cookies[CsrfCookie];
        if (!string.IsNullOrEmpty(csrf))
            ctx.Response.Cookies.Append(CsrfCookie, csrf, BaseOptions(ctx, httpOnly: false, expiresAt));
    }

    /// <summary>Xoá cả hai cookie (đăng xuất). Thuộc tính phải KHỚP lúc đặt, nếu không trình duyệt
    /// coi là cookie khác và cookie cũ vẫn nằm lại.</summary>
    public static void Clear(HttpContext ctx)
    {
        var opts = BaseOptions(ctx, httpOnly: true, null);
        ctx.Response.Cookies.Delete(AuthCookie, opts);
        ctx.Response.Cookies.Delete(CsrfCookie, BaseOptions(ctx, httpOnly: false, null));
    }

    /// <summary>Request này có xác thực BẰNG COOKIE hay không (khác với Bearer của app native).
    /// Chỉ request kiểu cookie mới cần kiểm tra CSRF — Bearer không tự động gửi kèm nên miễn nhiễm.</summary>
    public static bool UsesCookieAuth(HttpContext ctx)
        => !ctx.Request.Headers.ContainsKey("Authorization")
           && !string.IsNullOrEmpty(ctx.Request.Cookies[AuthCookie]);

    /// <summary>
    /// Origin của request có phải chính hệ thống này không.
    ///
    /// VÌ SAO CẦN: WebSocket KHÔNG bị CORS chặn. Khi phiên đi bằng cookie, một trang web lạ có thể mở
    /// WebSocket tới /hubs của ta và trình duyệt sẽ ngoan ngoãn đính cookie của người đang đăng nhập
    /// vào — nghĩa là trang đó nghe được tín hiệu realtime (kể cả tin nhắn) của nạn nhân. Đây là lỗ
    /// "Cross-Site WebSocket Hijacking", và nó CHỈ xuất hiện khi chuyển sang cookie: hồi còn Bearer
    /// thì trang lạ không có token nên không kết nối được.
    ///
    /// Header Origin do TRÌNH DUYỆT tự đặt, mã JavaScript không sửa được, nên đây là chốt đúng chỗ.
    /// Không có Origin ⇒ không phải trình duyệt (ứng dụng Android dùng Bearer) ⇒ để đường khác lo.
    /// </summary>
    public static bool IsAllowedOrigin(HttpContext ctx, string[] configuredOrigins)
    {
        var origin = ctx.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return true;

        var self = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        if (string.Equals(origin, self, StringComparison.OrdinalIgnoreCase)) return true;

        // Sau Cloudflare Tunnel, scheme mà Kestrel thấy có thể là http trong khi trình duyệt gửi
        // https — so thêm phần host để không tự chặn chính mình.
        if (origin.EndsWith($"://{ctx.Request.Host}", StringComparison.OrdinalIgnoreCase)) return true;

        return CorsPolicy.IsAllowed(origin, configuredOrigins);
    }

    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];

    /// <summary>
    /// Double-submit: header phải khớp cookie CSRF. Trang web lạ ép được trình duyệt GỬI cookie của ta
    /// nhưng KHÔNG ĐỌC được nó (khác origin), nên không dựng nổi header khớp.
    /// So sánh theo thời gian hằng số để không rò rỉ thông tin qua thời gian phản hồi.
    /// </summary>
    public static bool ValidateCsrf(HttpContext ctx)
    {
        if (SafeMethods.Contains(ctx.Request.Method)) return true;
        var cookie = ctx.Request.Cookies[CsrfCookie];
        var header = ctx.Request.Headers[CsrfHeader].ToString();
        if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(header)) return false;
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(cookie),
            System.Text.Encoding.UTF8.GetBytes(header));
    }
}
