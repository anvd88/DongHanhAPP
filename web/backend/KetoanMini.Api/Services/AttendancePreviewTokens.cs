using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace KetoanMini.Api.Services;

/// <summary>
/// Token XÁC NHẬN chấm công cấp ở bước xem trước.
///
/// Trước đây bước "Xác nhận" gửi lại ĐÚNG loạt ảnh đã xem trước với PreviewOnly=false, nên server chạy
/// lại toàn bộ khâu nặng lần thứ hai: chấm chất lượng mọi khung, Silent-Face 5 khung, trích vector AdaFace
/// R50 5 khung, rồi giải mã + so khớp toàn bộ bảng mẫu. Tức mỗi lượt chấm công tốn GẤP ĐÔI suy luận cần
/// thiết, dù kết quả nhận diện đã có sẵn từ vài giây trước.
///
/// Nay bước xem trước cấp một token ngắn hạn giữ sẵn kết quả (ai, độ khớp, vector đặc trưng); bước xác
/// nhận chỉ gửi token → server ghi công mà KHÔNG nhận diện lại.
///
/// Tính chất bảo mật giữ nguyên như cũ:
///   • Ràng buộc theo tài khoản đã đăng nhập — người khác cầm được token cũng không dùng được.
///   • DÙNG MỘT LẦN — lấy ra là xóa, không phát lại được.
///   • Sống rất ngắn (2 phút), vừa đủ cho người dùng đọc form xác nhận rồi bấm nút.
///   • Chỉ cấp SAU khi đã qua hết các cổng: tư thế, chất lượng, Silent-Face, quay đầu và so khớp đúng
///     người. Tức token không mở thêm đường nào — nó chỉ ghi nhớ một kết quả đã được duyệt đầy đủ.
/// </summary>
public sealed class AttendancePreviewTokens(IMemoryCache cache)
{
    // Đủ để đọc form xác nhận rồi bấm nút; quá hạn thì client quét lại (rẻ hơn nhiều so với rủi ro
    // giữ một "giấy phép ghi công" lâu trong RAM).
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    // Giữ phép "lấy ra và xóa" của Consume là nguyên tử (xem giải thích trong Consume).
    private readonly object _gate = new();

    /// <summary>
    /// Kết quả nhận diện đã chốt ở bước xem trước. <paramref name="Probe"/> là vector đặc trưng đã gộp —
    /// giữ lại để bước xác nhận vẫn TỰ HỌC được như luồng cũ mà không phải trích lại từ ảnh.
    /// </summary>
    public sealed record Pending(
        string Requester,
        string MatchedUser,
        string MatchedName,
        double Similarity,
        double Quality,
        float[] Probe);

    private static string CacheKey(string token) => "chamcong:preview:" + token;

    /// <summary>Cấp token cho một lượt xem trước đã nhận diện chắc chắn. Trả null nếu chưa đăng nhập.</summary>
    public string? Issue(Pending pending)
    {
        // Chỉ cấp cho request ĐÃ đăng nhập: token thay cho ảnh nên phải bám vào một danh tính cụ thể.
        // Kiosk ẩn danh không dùng bước xem trước, cứ giữ nguyên luồng gửi ảnh.
        if (string.IsNullOrWhiteSpace(pending.Requester)) return null;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set(CacheKey(token), pending, Ttl);
        return token;
    }

    /// <summary>
    /// Lấy RA kết quả xem trước (dùng một lần) nếu token còn hạn và đúng người đã xin cấp.
    /// Trả null khi token sai/hết hạn/nhầm người — gọi bên ngoài phải bắt người dùng quét lại.
    /// </summary>
    public Pending? Consume(string requester, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(requester)) return null;

        var key = CacheKey(token);

        // Đọc-rồi-xóa phải NGUYÊN TỬ. Không khóa thì hai request xác nhận gửi song song cùng một token
        // đều lọt qua TryGetValue trước khi cái nào kịp Remove ⇒ cả hai cùng ghi công, tức "dùng một lần"
        // chỉ đúng khi người dùng bấm chậm. IMemoryCache không có sẵn phép lấy-và-xóa nguyên tử nên tự
        // khóa; tranh chấp không đáng kể vì mỗi lượt chấm công chỉ chạm vào đây đúng một lần.
        Pending? pending;
        lock (_gate)
        {
            if (!cache.TryGetValue(key, out pending) || pending is null) return null;
            // Xóa TRƯỚC khi kiểm tra danh tính: token đã lộ ra ngoài thì coi như hỏng, không cho thử lại.
            cache.Remove(key);
        }

        return string.Equals(pending.Requester, requester, StringComparison.OrdinalIgnoreCase)
            ? pending
            : null;
    }
}
