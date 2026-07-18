using System.Security.Cryptography;

namespace KetoanMini.Api.Security;

/// <summary>
/// Sinh mã khôi phục mật khẩu do admin cấp (thay cho reset bằng khuôn mặt đã tắt). Mã một lần, dễ đọc,
/// nhóm 4 ký tự bằng dấu '-'. Chỉ dùng bảng chữ Crockford base32 (bỏ 0/O/1/I/L/U dễ nhầm) để đọc/gõ chính xác.
/// </summary>
public static class RecoveryCodes
{
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Sinh mã 12 ký tự dạng "XXXX-XXXX-XXXX".</summary>
    public static string Generate()
    {
        Span<char> chars = stackalloc char[12];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return $"{new string(chars[..4])}-{new string(chars[4..8])}-{new string(chars[8..])}";
    }

    /// <summary>Chuẩn hóa mã người dùng nhập (bỏ dấu cách/gạch, in hoa) để so khớp ổn định.</summary>
    public static string Normalize(string? code)
        => (code ?? "").Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
}
