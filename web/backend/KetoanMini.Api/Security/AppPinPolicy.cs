namespace KetoanMini.Api.Security;

/// <summary>
/// Quy tắc cho mã bảo mật 6 số của ứng dụng di động.
///
/// MÃ NẰM Ở MÁY CHỦ, KHÔNG CÒN BẢN SAO NÀO TRÊN THIẾT BỊ. Trước đây hash + salt + bộ đếm sai nằm
/// trong SharedPreferences của app: ai giữ được máy (hoặc ảnh sao lưu của máy) có thể dò thử 10^6 mã
/// thoải mái ngoại tuyến, và chỉ cần xoá dữ liệu app là bộ đếm sai/khoá thử lại về 0. Chuyển lên máy
/// chủ thì mọi lần thử phải đi qua mạng, đếm sai theo TÀI KHOẢN nên cài lại app không reset được, và
/// thiết bị mất cắp không mang theo thứ gì để dò.
///
/// Hash dùng chung <see cref="PasswordHasher"/> (Argon2id, memory-hard) — không gian mã chỉ có 6 chữ
/// số nên hàm băm rẻ như PBKDF2 vài vòng là dò xong trong tích tắc nếu CSDL bị lộ.
/// </summary>
public static class AppPinPolicy
{
    public const int PinLength = 6;

    /// <summary>Số lần thử sai liên tiếp trong một "cụm" trước khi bị khoá tạm.</summary>
    public const int AttemptsPerLock = 5;

    /// <summary>Đúng 6 chữ số (không dấu cách, không chữ).</summary>
    public static bool IsWellFormed(string? pin)
        => pin is { Length: PinLength } && pin.All(c => c is >= '0' and <= '9');

    /// <summary>
    /// Mã quá dễ đoán: toàn một chữ số (000000) hoặc dãy tăng/giảm liên tiếp (123456, 654321).
    /// Với không gian chỉ 10^6, vài chục mã kiểu này chiếm phần lớn số lần đoán trúng thực tế.
    /// </summary>
    public static bool IsTooObvious(string pin)
    {
        if (!IsWellFormed(pin)) return false;
        var allSame = true;
        var ascending = true;
        var descending = true;
        for (var i = 1; i < pin.Length; i++)
        {
            if (pin[i] != pin[0]) allSame = false;
            if (pin[i] != pin[i - 1] + 1) ascending = false;
            if (pin[i] != pin[i - 1] - 1) descending = false;
        }
        return allSame || ascending || descending;
    }

    /// <summary>
    /// Khoá thử lại tăng dần theo tổng số lần sai liên tiếp: 5 lần → 30 giây, 10 lần → 5 phút,
    /// 15 lần trở đi → 30 phút. Giữa các mốc không khoá để người gõ nhầm một hai lần không bị chặn.
    /// </summary>
    public static TimeSpan LockDuration(int failedAttempts) => failedAttempts switch
    {
        < AttemptsPerLock => TimeSpan.Zero,
        _ when failedAttempts % AttemptsPerLock != 0 => TimeSpan.Zero,
        >= 15 => TimeSpan.FromMinutes(30),
        >= 10 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromSeconds(30),
    };

    /// <summary>Còn mấy lần thử nữa thì bị khoá (dùng để báo cho người dùng).</summary>
    public static int AttemptsBeforeLock(int failedAttempts)
        => AttemptsPerLock - failedAttempts % AttemptsPerLock;

    /// <summary>Số giây còn lại của lần khoá hiện hành; 0 nếu không bị khoá.</summary>
    public static long SecondsRemaining(DateTime? lockedUntilUtc, DateTime nowUtc)
    {
        if (lockedUntilUtc is not { } until) return 0;
        var seconds = (long)Math.Ceiling((until - nowUtc).TotalSeconds);
        return seconds > 0 ? seconds : 0;
    }
}
