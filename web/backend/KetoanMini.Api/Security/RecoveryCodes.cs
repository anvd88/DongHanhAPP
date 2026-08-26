using System.Security.Cryptography;

namespace KetoanMini.Api.Security;

/// <summary>
/// Sinh mã khôi phục mật khẩu do admin cấp (thay cho reset bằng khuôn mặt đã tắt). Mã một lần, dễ đọc,
/// gọn 5 ký tự liền (không chia nhóm). Chỉ dùng bảng chữ Crockford base32 (bỏ 0/O/1/I/L/U dễ nhầm) để
/// đọc/gõ chính xác. Không gian mã 30^5 ≈ 24,3 triệu — an toàn nhờ mã hết hạn 7 ngày, dùng một lần và
/// endpoint đặt lại bị giới hạn 8 lần/10 phút.
/// </summary>
public static class RecoveryCodes
{
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Độ dài mã khôi phục (ký tự) — dùng chung cho sinh mã và kiểm tra đầu vào.</summary>
    public const int Length = 5;

    /// <summary>Sinh mã 5 ký tự dạng "XXXXX".</summary>
    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    /// <summary>Chuẩn hóa mã người dùng nhập (bỏ dấu cách/gạch, in hoa) để so khớp ổn định.</summary>
    public static string Normalize(string? code)
        => (code ?? "").Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
}
