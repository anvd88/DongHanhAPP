using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace KetoanMini.Api.Security;

/// <summary>
/// Băm mật khẩu (và mã khôi phục). Mặc định dùng <b>Argon2id</b> — hàm băm memory-hard, chống được
/// tấn công dò bằng GPU/ASIC tốt hơn nhiều PBKDF2.
///
/// TƯƠNG THÍCH NGƯỢC: các hash cũ đang nằm trong CSDL vẫn ở dạng PBKDF2 (và cả app desktop cũng sinh
/// PBKDF2). <see cref="Verify"/> nhận CẢ HAI định dạng nên không ai bị khóa ra ngoài. Việc di trú diễn
/// ra êm: khi người dùng đăng nhập đúng mật khẩu mà hash còn là PBKDF2, chỗ gọi băm lại bằng Argon2id
/// (xem <see cref="NeedsRehash"/>) — không cần bắt ai đổi mật khẩu.
///
/// Định dạng:
///   Argon2id: ARGON2ID$v=19$m=&lt;KiB&gt;,t=&lt;iterations&gt;,p=&lt;lanes&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;
///   PBKDF2  : PBKDF2$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;   (chỉ còn để đọc hash cũ)
/// </summary>
public static class PasswordHasher
{
    // ── Tham số Argon2id (khuyến nghị OWASP: m=19 MiB, t=2, p=1) ──────────────────────────────────
    // Máy phục vụ một văn phòng nhỏ, đăng nhập thưa và đã có rate-limit, nên 19 MiB/lần băm là dư sức.
    // Đổi các hằng này để tăng độ khó về sau: hash cũ vẫn verify được (tham số nằm trong chuỗi lưu),
    // và NeedsRehash sẽ báo cần băm lại lần đăng nhập kế tiếp.
    private const int Argon2MemoryKiB = 19_456; // 19 MiB
    private const int Argon2Iterations = 2;     // số lượt (t)
    private const int Argon2Parallelism = 1;    // số làn (p)
    private const string Argon2Prefix = "ARGON2ID";

    // ── Tham số PBKDF2 cũ (chỉ dùng để xác thực hash đã lưu; không sinh mới nữa) ──────────────────
    private const string Pbkdf2Prefix = "PBKDF2";

    private const int SaltSize = 16;
    private const int KeySize = 32;

    /// <summary>Sinh hash Argon2id cho mật khẩu/mã. Định dạng tự mô tả tham số để verify không phụ
    /// thuộc hằng số hiện hành (đổi độ khó không làm hỏng hash cũ).</summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Argon2Compute(password, salt, Argon2MemoryKiB, Argon2Iterations, Argon2Parallelism);
        return $"{Argon2Prefix}$v=19$m={Argon2MemoryKiB},t={Argon2Iterations},p={Argon2Parallelism}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Xác thực với hash đã lưu — nhận cả Argon2id (mới) lẫn PBKDF2 (cũ).</summary>
    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;
        if (storedHash.StartsWith(Argon2Prefix + "$", StringComparison.Ordinal))
            return VerifyArgon2(password, storedHash);
        if (storedHash.StartsWith(Pbkdf2Prefix + "$", StringComparison.Ordinal))
            return VerifyPbkdf2(password, storedHash);
        return false;
    }

    /// <summary>Hash này có nên được băm lại bằng Argon2id chuẩn hiện hành không? Đúng khi: (a) còn là
    /// PBKDF2/định dạng lạ, hoặc (b) là Argon2id nhưng tham số yếu hơn cấu hình hiện tại. Chỗ đăng nhập
    /// gọi hàm này sau khi Verify thành công (lúc còn giữ mật khẩu thô) để di trú dần.</summary>
    public static bool NeedsRehash(string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return true;
        if (!storedHash.StartsWith(Argon2Prefix + "$", StringComparison.Ordinal)) return true;
        if (!TryParseArgon2(storedHash, out _, out var m, out var t, out var p)) return true;
        return m < Argon2MemoryKiB || t < Argon2Iterations || p != Argon2Parallelism;
    }

    private static byte[] Argon2Compute(string password, byte[] salt, int memoryKiB, int iterations, int parallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(KeySize);
    }

    private static bool VerifyArgon2(string password, string storedHash)
    {
        if (!TryParseArgon2(storedHash, out var expected, out var m, out var t, out var p))
            return false;
        try
        {
            var salt = Convert.FromBase64String(storedHash.Split('$')[3]);
            var actual = Argon2Compute(password, salt, m, t, p);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Tách chuỗi Argon2id: lấy hash mong đợi + tham số m,t,p. Trả false nếu định dạng sai.</summary>
    private static bool TryParseArgon2(string storedHash, out byte[] expected, out int m, out int t, out int p)
    {
        expected = [];
        m = t = p = 0;
        var parts = storedHash.Split('$'); // ARGON2ID | v=19 | m=..,t=..,p=.. | salt | hash
        if (parts.Length != 5 || !string.Equals(parts[0], Argon2Prefix, StringComparison.Ordinal))
            return false;

        foreach (var kv in parts[2].Split(','))
        {
            var eq = kv.Split('=', 2);
            if (eq.Length != 2 || !int.TryParse(eq[1], out var val)) return false;
            switch (eq[0])
            {
                case "m": m = val; break;
                case "t": t = val; break;
                case "p": p = val; break;
            }
        }
        if (m <= 0 || t <= 0 || p <= 0) return false;

        try { expected = Convert.FromBase64String(parts[4]); }
        catch { return false; }
        return true;
    }

    private static bool VerifyPbkdf2(string password, string storedHash)
    {
        var parts = storedHash.Split('$'); // PBKDF2 | iterations | salt | hash
        if (parts.Length != 4 || !string.Equals(parts[0], Pbkdf2Prefix, StringComparison.Ordinal)
            || !int.TryParse(parts[1], out var iterations))
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }
}
